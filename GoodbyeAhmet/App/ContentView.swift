import SwiftUI

struct ContentView: View {
    @EnvironmentObject private var settings: AppSettings
    @EnvironmentObject private var vpn: VPNManager
    @State private var showsVPNDisclosure = false

    var body: some View {
        NavigationView {
            ZStack {
                LinearGradient(
                    colors: [Color(red: 0.035, green: 0.055, blue: 0.09), Color(red: 0.02, green: 0.12, blue: 0.17)],
                    startPoint: .topLeading,
                    endPoint: .bottomTrailing
                )
                .ignoresSafeArea()

                Circle()
                    .fill(Color.cyan.opacity(0.08))
                    .frame(width: 330, height: 330)
                    .blur(radius: 2)
                    .offset(x: 150, y: -310)

                VStack(spacing: 28) {
                    header
                    Spacer(minLength: 12)
                    connectButton
                    statusCard
                    Spacer()
                    footer
                }
                .padding(.horizontal, 24)
                .padding(.vertical, 18)
            }
            .navigationBarHidden(true)
        }
        .navigationViewStyle(.stack)
        .sheet(isPresented: $showsVPNDisclosure) {
            VPNDisclosureView {
                settings.acceptVPNDisclosure()
                showsVPNDisclosure = false
                Task { await vpn.connect(using: settings.configuration) }
            }
        }
    }

    private var header: some View {
        HStack(alignment: .center) {
            VStack(alignment: .leading, spacing: 4) {
                Text("GOODBYE AHMET")
                    .font(.system(size: 12, weight: .bold, design: .rounded))
                    .tracking(2.2)
                    .foregroundColor(.cyan)
                Text("Daha acik bir internet")
                    .font(.system(size: 25, weight: .semibold, design: .rounded))
                    .foregroundColor(.white)
            }

            Spacer()

            NavigationLink(destination: SettingsView()) {
                Image(systemName: "slider.horizontal.3")
                    .font(.system(size: 20, weight: .semibold))
                    .foregroundColor(.white)
                    .frame(width: 46, height: 46)
                    .background(Color.white.opacity(0.09), in: RoundedRectangle(cornerRadius: 15, style: .continuous))
            }
        }
    }

    private var connectButton: some View {
        Button {
            if vpn.status.isConnected || vpn.status.isBusy {
                Task { await vpn.toggle(using: settings.configuration) }
            } else if settings.hasAcceptedVPNDisclosure {
                Task { await vpn.connect(using: settings.configuration) }
            } else {
                showsVPNDisclosure = true
            }
        } label: {
            ZStack {
                Circle()
                    .stroke(vpn.status.isConnected ? Color.cyan.opacity(0.22) : Color.white.opacity(0.10), lineWidth: 18)
                    .frame(width: 226, height: 226)

                Circle()
                    .fill(
                        LinearGradient(
                            colors: vpn.status.isConnected
                                ? [Color.cyan, Color(red: 0.05, green: 0.55, blue: 0.92)]
                                : [Color.white.opacity(0.16), Color.white.opacity(0.07)],
                            startPoint: .topLeading,
                            endPoint: .bottomTrailing
                        )
                    )
                    .frame(width: 184, height: 184)
                    .shadow(color: vpn.status.isConnected ? Color.cyan.opacity(0.45) : .clear, radius: 34)

                VStack(spacing: 11) {
                    Image(systemName: "power")
                        .font(.system(size: 48, weight: .light))
                    Text(vpn.status.isConnected ? "KAPAT" : "BAGLAN")
                        .font(.system(size: 13, weight: .bold, design: .rounded))
                        .tracking(1.5)
                }
                .foregroundColor(.white)
            }
        }
        .buttonStyle(.plain)
        .disabled(vpn.status.isBusy)
        .overlay {
            if vpn.status.isBusy {
                ProgressView().tint(.white).scaleEffect(1.25)
            }
        }
    }

    private var statusCard: some View {
        VStack(spacing: 14) {
            HStack {
                Circle()
                    .fill(vpn.status.isConnected ? Color.cyan : Color.white.opacity(0.35))
                    .frame(width: 9, height: 9)
                Text(vpn.status.title)
                    .font(.system(size: 17, weight: .semibold, design: .rounded))
                    .foregroundColor(.white)
                Spacer()
                Text(settings.selectedPreset.name)
                    .font(.system(size: 13, weight: .medium, design: .rounded))
                    .foregroundColor(.cyan)
            }

            if let error = vpn.lastError {
                Text(error)
                    .font(.system(size: 12, weight: .regular, design: .rounded))
                    .foregroundColor(Color(red: 1, green: 0.55, blue: 0.42))
                    .frame(maxWidth: .infinity, alignment: .leading)
            } else {
                Text(settings.selectedPreset.detail)
                    .font(.system(size: 12, weight: .regular, design: .rounded))
                    .foregroundColor(.white.opacity(0.58))
                    .frame(maxWidth: .infinity, alignment: .leading)
            }
        }
        .padding(18)
        .background(Color.white.opacity(0.075), in: RoundedRectangle(cornerRadius: 20, style: .continuous))
        .overlay(RoundedRectangle(cornerRadius: 20, style: .continuous).stroke(Color.white.opacity(0.08)))
    }

    private var footer: some View {
        HStack(spacing: 8) {
            Image(systemName: settings.adBlockEnabled ? "shield.checkered" : "shield.slash")
            Text(settings.adBlockEnabled ? "Reklam engelleyici acik" : "Reklam engelleyici kapali")
        }
        .font(.system(size: 12, weight: .medium, design: .rounded))
        .foregroundColor(.white.opacity(0.52))
    }
}
