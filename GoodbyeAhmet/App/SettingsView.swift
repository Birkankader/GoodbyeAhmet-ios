import SwiftUI

struct SettingsView: View {
    @EnvironmentObject private var settings: AppSettings

    var body: some View {
        Form {
            Section("Baglanti profili") {
                Picker("Profil", selection: presetBinding) {
                    ForEach(VPNPreset.all) { preset in
                        Text(preset.name).tag(preset.id)
                    }
                }
                Text(settings.selectedPreset.detail)
                    .font(.footnote)
                    .foregroundColor(.secondary)
            }

            Section("DPI ayarlari") {
                Toggle("TLS ClientHello bol", isOn: $settings.splitClientHello)
                Stepper("Bolme konumu: \(settings.splitPosition)", value: $settings.splitPosition, in: 1...128)
                Toggle("HTTP Host harflerini karistir", isOn: $settings.mixHostCase)
            }

            Section("DNS") {
                TextField("DNS adresi (bos = sistem)", text: $settings.dnsAddress)
                    .textInputAutocapitalization(.never)
                    .disableAutocorrection(true)
                Stepper("Port: \(settings.dnsPort)", value: $settings.dnsPort, in: 1...65535)
            }

            Section {
                Toggle("Reklam engelleyici", isOn: $settings.adBlockEnabled)
                TextField("Hosts listesi URL", text: $settings.adBlockListURL)
                    .textInputAutocapitalization(.never)
                    .disableAutocorrection(true)
                    .keyboardType(.URL)
            } header: {
                Text("Reklam engelleme")
            } footer: {
                Text("Liste, VPN yeniden baglandiginda tunnel tarafinda indirilir ve 24 saat onbellekte tutulur.")
            }

            Section("Uygulama") {
                Toggle("Uygulama acilinca baglan", isOn: $settings.activateOnStart)
            }
        }
        .navigationTitle("Ayarlar")
        .navigationBarTitleDisplayMode(.inline)
    }

    private var presetBinding: Binding<String> {
        Binding(
            get: { settings.selectedPresetKey },
            set: { settings.applyPreset($0) }
        )
    }
}
