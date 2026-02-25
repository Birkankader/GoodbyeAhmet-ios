using Android.Content;
using Android.Net;

namespace GoodbyeAhmet.Mobile.Platforms.Android;

/// <summary>
/// Handles requesting VPN permission from the user via <see cref="VpnService.Prepare"/>
/// and starting/stopping the <see cref="DpiBypassVpnService"/>.
/// </summary>
public static class VpnHelper
{
    private const int VpnRequestCode = 1000;
    private static TaskCompletionSource<bool>? _permissionTcs;
    private static string? _pendingPresetKey;

    /// <summary>
    /// Requests VPN permission (if needed) and starts the VPN service.
    /// Returns true if the VPN was started successfully.
    /// </summary>
    public static async Task<bool> StartVpnAsync(string presetKey)
    {
        var activity = Platform.CurrentActivity;
        if (activity is null)
            return false;

        // Check if VPN permission is already granted
        var intent = VpnService.Prepare(activity);
        if (intent != null)
        {
            // Need user consent — show the system VPN dialog
            _pendingPresetKey = presetKey;
            _permissionTcs = new TaskCompletionSource<bool>();

            activity.StartActivityForResult(intent, VpnRequestCode);

            var granted = await _permissionTcs.Task;
            if (!granted)
                return false;
        }

        // Permission granted — start the service
        StartService(presetKey);
        return true;
    }

    /// <summary>
    /// Sends a disconnect intent to the VPN service.
    /// </summary>
    public static void StopVpn()
    {
        var context = Platform.AppContext;

        var intent = new Intent(context, typeof(DpiBypassVpnService));
        intent.SetAction(DpiBypassVpnService.ActionDisconnect);

        context.StartService(intent);
    }

    /// <summary>
    /// Must be called from <see cref="MainActivity.OnActivityResult"/> to handle
    /// the VPN consent dialog result.
    /// </summary>
    public static void OnActivityResult(int requestCode, global::Android.App.Result resultCode)
    {
        if (requestCode != VpnRequestCode) return;

        var granted = resultCode == global::Android.App.Result.Ok;
        _permissionTcs?.TrySetResult(granted);

        if (granted && _pendingPresetKey != null)
        {
            StartService(_pendingPresetKey);
        }

        _pendingPresetKey = null;
    }

    private static void StartService(string presetKey)
    {
        var context = Platform.AppContext;

        var intent = new Intent(context, typeof(DpiBypassVpnService));
        intent.SetAction(DpiBypassVpnService.ActionConnect);
        intent.PutExtra(DpiBypassVpnService.ExtraPresetKey, presetKey);

#pragma warning disable CA1416
        if (global::Android.OS.Build.VERSION.SdkInt >= global::Android.OS.BuildVersionCodes.O)
            context.StartForegroundService(intent);
        else
            context.StartService(intent);
#pragma warning restore CA1416
    }
}
