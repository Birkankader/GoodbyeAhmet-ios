using Android.App;
using Android.Content;
using Android.Content.PM;
using Android.Net;
using Android.OS;
using Android.Runtime;
using Java.IO;
using System.Collections.Concurrent;
using System.Net;
using System.Net.Sockets;

namespace GoodbyeAhmet.Mobile.Platforms.Android;

/// <summary>
/// Android VpnService that establishes a TUN interface and proxies TCP/UDP
/// traffic through real sockets, applying DPI bypass techniques on TLS
/// ClientHello and HTTP Host headers.
/// </summary>
[Service(
    Name = "com.goodbyeahmet.dpi.DpiBypassVpnService",
    Permission = "android.permission.BIND_VPN_SERVICE",
    ForegroundServiceType = ForegroundService.TypeSpecialUse,
    Exported = false)]
[IntentFilter(["android.net.VpnService"])]
public class DpiBypassVpnService : VpnService
{
    public const string ActionConnect = "com.goodbyeahmet.ACTION_CONNECT";
    public const string ActionDisconnect = "com.goodbyeahmet.ACTION_DISCONNECT";
    public const string ExtraPresetKey = "preset_key";

    private const string ChannelId = "goodbyeahmet_vpn";
    private const int NotificationId = 1;
    private const string TunAddress = "10.120.0.1";
    private const string TunRoute = "0.0.0.0";
    internal const int TunMtu = 1500;
    private const string DefaultDns = "8.8.8.8";

    private ParcelFileDescriptor? _tunInterface;
    private CancellationTokenSource? _cts;
    private Thread? _tunReadThread;

    // Track active TCP proxy sessions: (srcIP, srcPort, dstIP, dstPort) → session
    private readonly ConcurrentDictionary<string, TcpProxySession> _tcpSessions = new();

    public static event Action<bool>? ConnectionStateChanged;
    public static event Action<string>? LogReceived;
    public static bool IsRunning { get; private set; }

    private Models.BypassPreset? _activePreset;
    private FileOutputStream? _tunOutput;
    private readonly object _tunWriteLock = new();
    private int _tcpSessionCount;
    private int _udpForwardCount;
    private Services.DnsBlocklistService? _blocklist;

    public override StartCommandResult OnStartCommand(Intent? intent, StartCommandFlags flags, int startId)
    {
        var action = intent?.Action;
        if (action == ActionDisconnect)
        {
            Disconnect();
            return StartCommandResult.NotSticky;
        }
        if (action == ActionConnect)
        {
            var presetKey = intent?.GetStringExtra(ExtraPresetKey) ?? "default";
            Connect(presetKey);
        }
        return StartCommandResult.Sticky;
    }

    private void Connect(string presetKey)
    {
        if (_tunInterface != null)
        {
            EmitLog("Already connected — ignoring duplicate start.");
            return;
        }

        var presetService = IPlatformApplication.Current!.Services.GetRequiredService<Services.PresetService>();
        _activePreset = presetService.GetByKey(presetKey);
        if (_activePreset is null)
        {
            EmitLog($"Unknown preset '{presetKey}', using default.");
            _activePreset = presetService.GetByKey("default")!;
        }

        // Override preset values with any custom settings saved by the user
        var settings = IPlatformApplication.Current!.Services.GetRequiredService<Services.SettingsService>();
        if (int.TryParse(settings.Ttl, out var customTtl) && customTtl > 0)
            _activePreset.FakeTtl = customTtl;
        if (int.TryParse(settings.SplitPosition, out var customSplit) && customSplit > 0)
            _activePreset.SplitPosition = customSplit;
        _activePreset.SplitClientHello = settings.SplitClientHello;
        _activePreset.MixHostCase = settings.MixHostCase;
        if (!string.IsNullOrEmpty(settings.DnsV4Address))
        {
            _activePreset.DnsRedirectAddress = settings.DnsV4Address;
            if (int.TryParse(settings.DnsV4Port, out var p) && p > 0)
                _activePreset.DnsRedirectPort = p;
        }

        // Initialize DNS ad-blocking (Pi-hole sinkhole)
        _blocklist = IPlatformApplication.Current!.Services.GetRequiredService<Services.DnsBlocklistService>();
        if (settings.AdBlockEnabled && _blocklist.IsEnabled)
        {
            EmitLog($"Ad-blocker active — {_blocklist.DomainCount} domains loaded.");
            _blocklist.ResetCounter();
        }

        CreateNotificationChannel();
        var notification = BuildNotification($"Active — {_activePreset.DisplayName}");

#pragma warning disable CA1416
        if (Build.VERSION.SdkInt >= BuildVersionCodes.UpsideDownCake)
            StartForeground(NotificationId, notification, ForegroundService.TypeSpecialUse);
        else if (Build.VERSION.SdkInt >= BuildVersionCodes.Q)
            StartForeground(NotificationId, notification, global::Android.Content.PM.ForegroundService.TypeNone);
        else
            StartForeground(NotificationId, notification);
#pragma warning restore CA1416

        var dns = string.IsNullOrEmpty(_activePreset.DnsRedirectAddress) ? DefaultDns : _activePreset.DnsRedirectAddress;

        var builder = new Builder(this)
            .SetSession("GoodbyeAhmet")
            .SetMtu(TunMtu)
            .AddAddress(TunAddress, 32)
            .AddRoute(TunRoute, 0)
            .AddDnsServer(dns);

        try { builder.AddDisallowedApplication(ApplicationContext!.PackageName!); } catch { }

        _tunInterface = builder.Establish();
        if (_tunInterface == null)
        {
            EmitLog("Failed to establish VPN interface.");
            StopSelf();
            return;
        }

        _tunOutput = new FileOutputStream(_tunInterface.FileDescriptor);

        EmitLog($"TUN up. DNS={dns}, Preset={_activePreset.DisplayName}");
        EmitLog($"  SplitHello={_activePreset.SplitClientHello}@{_activePreset.SplitPosition}, MixCase={_activePreset.MixHostCase}, TTL={_activePreset.FakeTtl}");

        IsRunning = true;
        ConnectionStateChanged?.Invoke(true);

        _cts = new CancellationTokenSource();
        _tunReadThread = new Thread(() => TunReadLoop(_cts.Token))
        {
            Name = "TUN-Reader",
            IsBackground = true
        };
        _tunReadThread.Start();
    }

    private void Disconnect()
    {
        EmitLog("Disconnecting…");
        _cts?.Cancel();

        // Close all TCP sessions
        foreach (var kv in _tcpSessions)
        {
            try { kv.Value.Close(); } catch { }
        }
        _tcpSessions.Clear();

        try { _tunOutput?.Close(); } catch { }
        _tunOutput = null;
        try { _tunInterface?.Close(); } catch { }
        _tunInterface = null;

        IsRunning = false;
        ConnectionStateChanged?.Invoke(false);
        StopForeground(StopForegroundFlags.Remove);
        StopSelf();
        EmitLog("VPN disconnected.");
    }

    public override void OnDestroy()
    {
        Disconnect();
        base.OnDestroy();
    }

    // ═══════════════════════════════════════════════════════════
    //  TUN Read Loop — reads IP packets, dispatches TCP/UDP
    // ═══════════════════════════════════════════════════════════

    private void TunReadLoop(CancellationToken ct)
    {
        EmitLog("Packet processing started.");
        var input = new FileInputStream(_tunInterface!.FileDescriptor);
        var buf = new byte[TunMtu];

        try
        {
            while (!ct.IsCancellationRequested)
            {
                int len = input.Read(buf, 0, buf.Length);
                if (len <= 0) { Thread.Sleep(1); continue; }

                // Parse IP header
                byte version = (byte)((buf[0] >> 4) & 0xF);
                if (version != 4) continue; // IPv4 only for now

                int ihl = (buf[0] & 0xF) * 4;
                if (len < ihl) continue;

                byte protocol = buf[9];
                var srcIp = new IPAddress(new ReadOnlySpan<byte>(buf, 12, 4));
                var dstIp = new IPAddress(new ReadOnlySpan<byte>(buf, 16, 4));

                if (protocol == 6) // TCP
                {
                    HandleTcpPacket(buf, len, ihl, srcIp, dstIp);
                }
                else if (protocol == 17) // UDP
                {
                    HandleUdpPacket(buf, len, ihl, srcIp, dstIp);
                }
                // Other protocols (ICMP etc.) are silently dropped
            }
        }
        catch (Java.IO.IOException) when (ct.IsCancellationRequested) { }
        catch (Exception ex)
        {
            if (!ct.IsCancellationRequested)
                EmitLog($"TUN read error: {ex.Message}");
        }
        finally
        {
            EmitLog("Packet processing stopped.");
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  TCP Handling — SYN triggers real connection, data is proxied
    // ═══════════════════════════════════════════════════════════

    private void HandleTcpPacket(byte[] pkt, int pktLen, int ihl, IPAddress srcIp, IPAddress dstIp)
    {
        if (pktLen < ihl + 20) return; // Too short for TCP header

        int srcPort = (pkt[ihl] << 8) | pkt[ihl + 1];
        int dstPort = (pkt[ihl + 2] << 8) | pkt[ihl + 3];
        int dataOffset = ((pkt[ihl + 12] >> 4) & 0xF) * 4;
        byte flags = pkt[ihl + 13];
        bool syn = (flags & 0x02) != 0;
        bool ack = (flags & 0x10) != 0;
        bool fin = (flags & 0x01) != 0;
        bool rst = (flags & 0x04) != 0;

        uint seqNum = (uint)((pkt[ihl + 4] << 24) | (pkt[ihl + 5] << 16) | (pkt[ihl + 6] << 8) | pkt[ihl + 7]);
        uint ackNum = (uint)((pkt[ihl + 8] << 24) | (pkt[ihl + 9] << 16) | (pkt[ihl + 10] << 8) | pkt[ihl + 11]);

        string key = $"{srcIp}:{srcPort}-{dstIp}:{dstPort}";

        if (syn && !ack)
        {
            // New connection — SYN
            var session = new TcpProxySession(this, srcIp, srcPort, dstIp, dstPort, seqNum, _activePreset!);
            _tcpSessions[key] = session;
            Interlocked.Increment(ref _tcpSessionCount);

            // Send SYN-ACK back to TUN
            session.SendSynAck();

            // Connect to real server in background
            _ = Task.Run(() => session.ConnectToServerAsync());
            return;
        }

        if (!_tcpSessions.TryGetValue(key, out var sess)) return;

        if (rst)
        {
            sess.Close();
            _tcpSessions.TryRemove(key, out _);
            return;
        }

        if (fin)
        {
            sess.HandleFin(seqNum);
            _tcpSessions.TryRemove(key, out _);
            return;
        }

        // ACK with data — forward to real server
        int payloadStart = ihl + dataOffset;
        int payloadLen = pktLen - payloadStart;
        if (payloadLen > 0 && ack)
        {
            byte[] payload = new byte[payloadLen];
            Buffer.BlockCopy(pkt, payloadStart, payload, 0, payloadLen);
            sess.OnClientData(payload, seqNum);
        }
        else if (ack)
        {
            sess.OnClientAck(ackNum);
        }
    }

    // ═══════════════════════════════════════════════════════════
    //  UDP Handling — primarily DNS forwarding
    // ═══════════════════════════════════════════════════════════

    private void HandleUdpPacket(byte[] pkt, int pktLen, int ihl, IPAddress srcIp, IPAddress dstIp)
    {
        if (pktLen < ihl + 8) return;

        int srcPort = (pkt[ihl] << 8) | pkt[ihl + 1];
        int dstPort = (pkt[ihl + 2] << 8) | pkt[ihl + 3];
        int udpLen = (pkt[ihl + 4] << 8) | pkt[ihl + 5];
        int payloadStart = ihl + 8;
        int payloadLen = pktLen - payloadStart;
        if (payloadLen <= 0) return;

        // ── DNS Ad-Block Sinkhole ────────────────────────────
        if (dstPort == 53 && _blocklist is { IsEnabled: true })
        {
            var dnsPayload = new byte[payloadLen];
            Buffer.BlockCopy(pkt, payloadStart, dnsPayload, 0, payloadLen);

            var domain = Services.DnsPacketHelper.ParseQueryDomain(dnsPayload, payloadLen);
            if (domain != null && _blocklist.IsBlocked(domain))
            {
                // Craft spoofed response → 0.0.0.0 / :: / NXDOMAIN
                var spoofed = Services.DnsPacketHelper.CraftBlockedResponse(dnsPayload, payloadLen);
                if (spoofed != null)
                {
                    EmitLog($"[AdBlock] Blocked: {domain}");
                    // Write spoofed DNS response back to TUN
                    // (swap src/dst so the response goes back to the app)
                    WriteUdpResponseToTun(dstIp, dstPort, srcIp, srcPort, spoofed);
                    return; // Do NOT forward to real DNS server
                }
            }
        }

        // Forward DNS and other UDP
        _ = Task.Run(async () =>
        {
            try
            {
                var payload = new byte[payloadLen];
                Buffer.BlockCopy(pkt, payloadStart, payload, 0, payloadLen);

                // Determine real destination
                IPAddress realDst = dstIp;
                int realPort = dstPort;

                // If DNS redirect is configured and this is DNS traffic (port 53)
                if (dstPort == 53 && _activePreset?.DnsRedirectAddress != null)
                {
                    realDst = IPAddress.Parse(_activePreset.DnsRedirectAddress);
                    realPort = _activePreset.DnsRedirectPort;
                }

                using var udpClient = new UdpClient();
                Protect(udpClient.Client.Handle.ToInt32()); // Bypass VPN for real socket
                udpClient.Client.ReceiveTimeout = 5000;

                await udpClient.SendAsync(payload, payload.Length, new IPEndPoint(realDst, realPort));
                var result = await udpClient.ReceiveAsync();

                Interlocked.Increment(ref _udpForwardCount);

                // Build response IP+UDP packet back to TUN
                WriteUdpResponseToTun(dstIp, dstPort, srcIp, srcPort, result.Buffer);
            }
            catch { /* DNS timeout or error, silently drop */ }
        });
    }

    // ═══════════════════════════════════════════════════════════
    //  Packet Construction Helpers
    // ═══════════════════════════════════════════════════════════

    internal void WriteTcpPacketToTun(
        IPAddress srcIp, int srcPort,
        IPAddress dstIp, int dstPort,
        uint seqNum, uint ackNum,
        byte flags, byte[]? payload = null)
    {
        int payloadLen = payload?.Length ?? 0;
        int tcpHeaderLen = 20;
        int ipHeaderLen = 20;
        int totalLen = ipHeaderLen + tcpHeaderLen + payloadLen;

        var pkt = new byte[totalLen];

        // IP header
        pkt[0] = 0x45; // IPv4, IHL=5
        pkt[2] = (byte)(totalLen >> 8);
        pkt[3] = (byte)(totalLen & 0xFF);
        pkt[5] = 1; // identification
        pkt[8] = 64; // TTL
        pkt[9] = 6;  // TCP
        var srcBytes = srcIp.GetAddressBytes();
        var dstBytes = dstIp.GetAddressBytes();
        Buffer.BlockCopy(srcBytes, 0, pkt, 12, 4);
        Buffer.BlockCopy(dstBytes, 0, pkt, 16, 4);
        // IP checksum
        SetChecksum(pkt, 0, 20);

        // TCP header
        int t = ipHeaderLen;
        pkt[t] = (byte)(srcPort >> 8);
        pkt[t + 1] = (byte)(srcPort & 0xFF);
        pkt[t + 2] = (byte)(dstPort >> 8);
        pkt[t + 3] = (byte)(dstPort & 0xFF);
        pkt[t + 4] = (byte)(seqNum >> 24);
        pkt[t + 5] = (byte)(seqNum >> 16);
        pkt[t + 6] = (byte)(seqNum >> 8);
        pkt[t + 7] = (byte)(seqNum & 0xFF);
        pkt[t + 8] = (byte)(ackNum >> 24);
        pkt[t + 9] = (byte)(ackNum >> 16);
        pkt[t + 10] = (byte)(ackNum >> 8);
        pkt[t + 11] = (byte)(ackNum & 0xFF);
        pkt[t + 12] = (byte)(5 << 4); // data offset = 5 (20 bytes)
        pkt[t + 13] = flags;
        pkt[t + 14] = 0xFF; // window size high
        pkt[t + 15] = 0xFF; // window size low

        if (payload != null)
            Buffer.BlockCopy(payload, 0, pkt, t + tcpHeaderLen, payloadLen);

        // TCP checksum (with pseudo-header)
        SetTcpChecksum(pkt, ipHeaderLen, totalLen - ipHeaderLen, srcBytes, dstBytes);

        WritePktToTun(pkt, totalLen);
    }

    private void WriteUdpResponseToTun(IPAddress srcIp, int srcPort, IPAddress dstIp, int dstPort, byte[] payload)
    {
        int ipHeaderLen = 20;
        int udpHeaderLen = 8;
        int totalLen = ipHeaderLen + udpHeaderLen + payload.Length;

        var pkt = new byte[totalLen];

        // IP header
        pkt[0] = 0x45;
        pkt[2] = (byte)(totalLen >> 8);
        pkt[3] = (byte)(totalLen & 0xFF);
        pkt[8] = 64;
        pkt[9] = 17; // UDP
        var srcBytes = srcIp.GetAddressBytes();
        var dstBytes = dstIp.GetAddressBytes();
        Buffer.BlockCopy(srcBytes, 0, pkt, 12, 4);
        Buffer.BlockCopy(dstBytes, 0, pkt, 16, 4);
        SetChecksum(pkt, 0, 20);

        // UDP header
        int u = ipHeaderLen;
        int udpLen = udpHeaderLen + payload.Length;
        pkt[u] = (byte)(srcPort >> 8);
        pkt[u + 1] = (byte)(srcPort & 0xFF);
        pkt[u + 2] = (byte)(dstPort >> 8);
        pkt[u + 3] = (byte)(dstPort & 0xFF);
        pkt[u + 4] = (byte)(udpLen >> 8);
        pkt[u + 5] = (byte)(udpLen & 0xFF);
        // UDP checksum = 0 (optional for IPv4)

        Buffer.BlockCopy(payload, 0, pkt, u + udpHeaderLen, payload.Length);

        WritePktToTun(pkt, totalLen);
    }

    internal void WritePktToTun(byte[] pkt, int len)
    {
        lock (_tunWriteLock)
        {
            try { _tunOutput?.Write(pkt, 0, len); }
            catch { /* TUN closed */ }
        }
    }

    private static void SetChecksum(byte[] buf, int offset, int length)
    {
        // Zero out checksum field first
        buf[offset + 10] = 0;
        buf[offset + 11] = 0;

        uint sum = 0;
        for (int i = 0; i < length; i += 2)
        {
            sum += (uint)(buf[offset + i] << 8);
            if (i + 1 < length)
                sum += buf[offset + i + 1];
        }
        while ((sum >> 16) != 0)
            sum = (sum & 0xFFFF) + (sum >> 16);
        ushort checksum = (ushort)~sum;
        buf[offset + 10] = (byte)(checksum >> 8);
        buf[offset + 11] = (byte)(checksum & 0xFF);
    }

    private static void SetTcpChecksum(byte[] pkt, int tcpOffset, int tcpLength, byte[] srcIp, byte[] dstIp)
    {
        // Zero the TCP checksum field
        pkt[tcpOffset + 16] = 0;
        pkt[tcpOffset + 17] = 0;

        // Pseudo-header
        uint sum = 0;
        sum += (uint)(srcIp[0] << 8 | srcIp[1]);
        sum += (uint)(srcIp[2] << 8 | srcIp[3]);
        sum += (uint)(dstIp[0] << 8 | dstIp[1]);
        sum += (uint)(dstIp[2] << 8 | dstIp[3]);
        sum += 6; // TCP protocol
        sum += (uint)tcpLength;

        // TCP segment
        for (int i = 0; i < tcpLength; i += 2)
        {
            sum += (uint)(pkt[tcpOffset + i] << 8);
            if (i + 1 < tcpLength)
                sum += pkt[tcpOffset + i + 1];
        }

        while ((sum >> 16) != 0)
            sum = (sum & 0xFFFF) + (sum >> 16);

        ushort checksum = (ushort)~sum;
        pkt[tcpOffset + 16] = (byte)(checksum >> 8);
        pkt[tcpOffset + 17] = (byte)(checksum & 0xFF);
    }

    internal void RemoveSession(string key)
    {
        _tcpSessions.TryRemove(key, out _);
    }

    // ═══════════════════════════════════════════════════════════
    //  Notification
    // ═══════════════════════════════════════════════════════════

    private void CreateNotificationChannel()
    {
        if (Build.VERSION.SdkInt < BuildVersionCodes.O) return;
#pragma warning disable CA1416
        var channel = new NotificationChannel(ChannelId, "GoodbyeAhmet VPN", NotificationImportance.Low)
        { Description = "Shows while the DPI bypass VPN is active" };
        ((NotificationManager?)GetSystemService(NotificationService))?.CreateNotificationChannel(channel);
#pragma warning restore CA1416
    }

    private Notification BuildNotification(string text)
    {
        var pendingIntent = PendingIntent.GetActivity(this, 0,
            ApplicationContext!.PackageManager!.GetLaunchIntentForPackage(ApplicationContext.PackageName!),
            PendingIntentFlags.Immutable);
#pragma warning disable CA1416
        return new Notification.Builder(this, ChannelId)
            .SetContentTitle("GoodbyeAhmet")
            .SetContentText(text)
            .SetSmallIcon(global::Android.Resource.Drawable.IcMenuManage)
            .SetContentIntent(pendingIntent)
            .SetOngoing(true)
            .Build();
#pragma warning restore CA1416
    }

    internal static void EmitLog(string msg)
    {
        global::Android.Util.Log.Debug("GoodbyeAhmet", msg);
        LogReceived?.Invoke(msg);
    }
}

// ═══════════════════════════════════════════════════════════════
//  TCP Proxy Session — connects to real server, applies DPI bypass
// ═══════════════════════════════════════════════════════════════

internal class TcpProxySession
{
    private readonly DpiBypassVpnService _vpn;
    private readonly IPAddress _clientIp;
    private readonly int _clientPort;
    private readonly IPAddress _serverIp;
    private readonly int _serverPort;
    private readonly Models.BypassPreset _preset;
    private readonly string _key;

    private Socket? _serverSocket;
    private bool _closed;
    private bool _firstPayloadSent;

    // Sequence tracking (simplified)
    private uint _clientSeq;  // next expected seq from client
    private uint _serverSeq;  // our seq toward client (server→client direction)

    private const byte TcpSyn = 0x02;
    private const byte TcpAck = 0x10;
    private const byte TcpSynAck = 0x12;
    private const byte TcpFin = 0x01;
    private const byte TcpFinAck = 0x11;
    private const byte TcpRst = 0x04;
    private const byte TcpPshAck = 0x18;

    public TcpProxySession(DpiBypassVpnService vpn, IPAddress clientIp, int clientPort,
        IPAddress serverIp, int serverPort, uint clientIsn, Models.BypassPreset preset)
    {
        _vpn = vpn;
        _clientIp = clientIp;
        _clientPort = clientPort;
        _serverIp = serverIp;
        _serverPort = serverPort;
        _preset = preset;
        _key = $"{clientIp}:{clientPort}-{serverIp}:{serverPort}";

        _clientSeq = clientIsn + 1; // After SYN, next expected is ISN+1
        _serverSeq = 1000; // Our ISN
    }

    public void SendSynAck()
    {
        _vpn.WriteTcpPacketToTun(
            _serverIp, _serverPort,
            _clientIp, _clientPort,
            _serverSeq, _clientSeq,
            TcpSynAck);
        _serverSeq++; // SYN consumes 1 seq
    }

    public async Task ConnectToServerAsync()
    {
        try
        {
            _serverSocket = new Socket(AddressFamily.InterNetwork, System.Net.Sockets.SocketType.Stream, ProtocolType.Tcp);
            _vpn.Protect(_serverSocket.Handle.ToInt32()); // Bypass VPN routing for outbound

            await _serverSocket.ConnectAsync(new IPEndPoint(_serverIp, _serverPort));

            // Start reading from server
            _ = Task.Run(ReadFromServerLoop);
        }
        catch (Exception ex)
        {
            DpiBypassVpnService.EmitLog($"Connect failed {_serverIp}:{_serverPort}: {ex.Message}");
            SendRst();
            Close();
        }
    }

    public void OnClientData(byte[] data, uint seq)
    {
        if (_closed || _serverSocket == null || !_serverSocket.Connected) return;

        _clientSeq = seq + (uint)data.Length;

        // Send ACK back to client
        _vpn.WriteTcpPacketToTun(
            _serverIp, _serverPort,
            _clientIp, _clientPort,
            _serverSeq, _clientSeq,
            TcpAck);

        try
        {
            // Apply DPI bypass on first payload
            if (!_firstPayloadSent)
            {
                _firstPayloadSent = true;
                SendWithDpiBypass(data);
            }
            else
            {
                _serverSocket.Send(data);
            }
        }
        catch
        {
            Close();
        }
    }

    public void OnClientAck(uint ackNum)
    {
        // Client acknowledged our data — nothing special to do
    }

    public void HandleFin(uint seq)
    {
        _clientSeq = seq + 1;
        // Send FIN-ACK back
        _vpn.WriteTcpPacketToTun(
            _serverIp, _serverPort,
            _clientIp, _clientPort,
            _serverSeq, _clientSeq,
            TcpFinAck);
        _serverSeq++;
        Close();
    }

    /// <summary>
    /// Applies DPI bypass techniques to the first TCP payload (typically TLS ClientHello or HTTP request).
    /// </summary>
    private void SendWithDpiBypass(byte[] data)
    {
        bool isTls = data.Length > 5 && data[0] == 0x16 && data[1] == 0x03;
        bool isHttp = data.Length > 4 && IsHttpMethod(data);

        if (isTls && _preset.SplitClientHello)
        {
            // Split TLS ClientHello into multiple TCP segments
            int splitPos = Math.Min(_preset.SplitPosition, data.Length - 1);
            if (splitPos < 1) splitPos = 1;

            // Set low TTL on first fragment if configured
            if (_preset.FakeTtl > 0)
            {
                SetSocketTtl(_preset.FakeTtl);
                _serverSocket!.Send(data, 0, splitPos, SocketFlags.None);
                SetSocketTtl(64); // restore normal TTL
            }
            else
            {
                _serverSocket!.Send(data, 0, splitPos, SocketFlags.None);
            }

            // Small delay between fragments to ensure they're in separate TCP segments
            Thread.Sleep(1);

            // Send remainder
            _serverSocket.Send(data, splitPos, data.Length - splitPos, SocketFlags.None);

            DpiBypassVpnService.EmitLog($"TLS split @{splitPos} → {_serverIp}:{_serverPort}");
        }
        else if (isHttp && _preset.MixHostCase)
        {
            // Mix case of HTTP Host header
            var modified = MixHttpHostCase(data);
            _serverSocket!.Send(modified);

            DpiBypassVpnService.EmitLog($"HTTP host-mix → {_serverIp}:{_serverPort}");
        }
        else if (_preset.FakeTtl > 0)
        {
            // Just use fake TTL on first packet
            SetSocketTtl(_preset.FakeTtl);
            _serverSocket!.Send(data);
            SetSocketTtl(64);
        }
        else
        {
            _serverSocket!.Send(data);
        }
    }

    private void SetSocketTtl(int ttl)
    {
        try { _serverSocket?.SetSocketOption(SocketOptionLevel.IP, SocketOptionName.IpTimeToLive, ttl); }
        catch { /* Not critical */ }
    }

    private static bool IsHttpMethod(byte[] data)
    {
        // GET, POST, HEAD, PUT, DELETE, CONNECT, OPTIONS, PATCH
        return (data[0] == 'G' && data[1] == 'E' && data[2] == 'T') ||
               (data[0] == 'P' && data[1] == 'O' && data[2] == 'S') ||
               (data[0] == 'H' && data[1] == 'E' && data[2] == 'A') ||
               (data[0] == 'P' && data[1] == 'U' && data[2] == 'T') ||
               (data[0] == 'D' && data[1] == 'E' && data[2] == 'L') ||
               (data[0] == 'C' && data[1] == 'O' && data[2] == 'N') ||
               (data[0] == 'O' && data[1] == 'P' && data[2] == 'T') ||
               (data[0] == 'P' && data[1] == 'A' && data[2] == 'T');
    }

    private static byte[] MixHttpHostCase(byte[] data)
    {
        var result = new byte[data.Length];
        Buffer.BlockCopy(data, 0, result, 0, data.Length);

        // Find "Host: " header and mix case of the value
        for (int i = 0; i < result.Length - 6; i++)
        {
            if ((result[i] == 'H' || result[i] == 'h') &&
                (result[i + 1] == 'o' || result[i + 1] == 'O') &&
                (result[i + 2] == 's' || result[i + 2] == 'S') &&
                (result[i + 3] == 't' || result[i + 3] == 'T') &&
                result[i + 4] == ':')
            {
                // Mix case of "Host" keyword itself: hOsT
                result[i] = (byte)'h';
                result[i + 1] = (byte)'O';
                result[i + 2] = (byte)'s';
                result[i + 3] = (byte)'T';
                break;
            }
        }

        return result;
    }

    private async Task ReadFromServerLoop()
    {
        var buf = new byte[DpiBypassVpnService.TunMtu - 40]; // Leave room for IP+TCP headers
        try
        {
            while (!_closed && _serverSocket != null && _serverSocket.Connected)
            {
                int read = await _serverSocket.ReceiveAsync(buf, SocketFlags.None);
                if (read <= 0) break;

                // Send data back to client via TUN
                byte[] payload = new byte[read];
                Buffer.BlockCopy(buf, 0, payload, 0, read);

                _vpn.WriteTcpPacketToTun(
                    _serverIp, _serverPort,
                    _clientIp, _clientPort,
                    _serverSeq, _clientSeq,
                    TcpPshAck,
                    payload);

                _serverSeq += (uint)read;
            }
        }
        catch { }
        finally
        {
            if (!_closed)
            {
                // Send FIN to client
                _vpn.WriteTcpPacketToTun(
                    _serverIp, _serverPort,
                    _clientIp, _clientPort,
                    _serverSeq, _clientSeq,
                    TcpFinAck);
                _serverSeq++;
                Close();
            }
        }
    }

    private void SendRst()
    {
        _vpn.WriteTcpPacketToTun(
            _serverIp, _serverPort,
            _clientIp, _clientPort,
            _serverSeq, _clientSeq,
            TcpRst);
    }

    public void Close()
    {
        if (_closed) return;
        _closed = true;
        try { _serverSocket?.Shutdown(SocketShutdown.Both); } catch { }
        try { _serverSocket?.Close(); } catch { }
        _serverSocket = null;
        _vpn.RemoveSession(_key);
    }

    // TcpProxySession uses DpiBypassVpnService.TunMtu directly
}
