import SwiftUI

struct VPNDisclosureView: View {
    @Environment(\.dismiss) private var dismiss
    let onAccept: () -> Void

    var body: some View {
        NavigationView {
            ScrollView {
                VStack(alignment: .leading, spacing: 18) {
                    Label("VPN Veri Bildirimi", systemImage: "shield.checkered")
                        .font(.title2.bold())

                    Text("GoodbyeAhmet, engelleme yöntemlerini aşmak için cihaz trafiğini yerel bir Packet Tunnel üzerinden işler. Uzak bir VPN sunucusuna bağlanmaz ve genel IP adresinizi değiştirmez.")

                    disclosureRow(
                        icon: "internaldrive",
                        title: "Cihaz üzerinde işleme",
                        text: "Paketler yalnızca yönlendirme, TLS bölme ve DNS reklam engelleme için geçici bellekte işlenir. Tarama geçmişi veya paket içeriği saklanmaz."
                    )
                    disclosureRow(
                        icon: "hand.raised",
                        title: "Veri toplanmaz",
                        text: "Geliştirici kullanıcı trafiğini toplamaz, satmaz, başka amaçla kullanmaz veya üçüncü taraflarla paylaşmaz. Analitik ve reklam SDK'sı yoktur."
                    )
                    disclosureRow(
                        icon: "arrow.down.circle",
                        title: "Blocklist indirmesi",
                        text: "Reklam engelleme açıksa seçilen HTTPS sunucusundan hosts listesi indirilir. Sunucu, standart bağlantı bilgilerini (ör. IP adresi) kendi politikası kapsamında görebilir."
                    )

                    NavigationLink("Gizlilik politikasının tamamını oku") {
                        PrivacyPolicyView()
                    }
                    .font(.callout.weight(.semibold))

                    Button("Kabul Et ve Bağlan", action: onAccept)
                        .buttonStyle(.borderedProminent)
                        .controlSize(.large)
                        .frame(maxWidth: .infinity)
                }
                .padding(24)
            }
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Vazgeç") { dismiss() }
                }
            }
        }
        .navigationViewStyle(.stack)
    }

    private func disclosureRow(icon: String, title: String, text: String) -> some View {
        HStack(alignment: .top, spacing: 12) {
            Image(systemName: icon)
                .foregroundColor(.accentColor)
                .frame(width: 24)
            VStack(alignment: .leading, spacing: 4) {
                Text(title).font(.headline)
                Text(text).font(.subheadline).foregroundColor(.secondary)
            }
        }
    }
}

struct PrivacyPolicyView: View {
    var body: some View {
        ScrollView {
            VStack(alignment: .leading, spacing: 18) {
                Text("Gizlilik Politikası")
                    .font(.title.bold())
                Text("Yürürlük tarihi: 19 Haziran 2026")
                    .font(.footnote)
                    .foregroundColor(.secondary)

                policySection(
                    "Veri toplama",
                    "GoodbyeAhmet; kimlik, konum, cihaz tanımlayıcısı, tarama geçmişi, DNS sorgusu veya paket içeriği toplamaz. Uygulamada hesap, analitik, reklam ya da izleme SDK'sı bulunmaz."
                )
                policySection(
                    "VPN trafiği",
                    "Ağ trafiği, hizmeti sağlamak amacıyla cihazdaki Packet Tunnel Extension içinde geçici olarak işlenir ve hedef sunucuya doğrudan iletilir. Geliştirici tarafından işletilen bir proxy veya VPN sunucusuna gönderilmez; kaydedilmez, satılmaz, başka amaçla kullanılmaz ve üçüncü taraflara açıklanmaz."
                )
                policySection(
                    "Yerel ayarlar",
                    "Seçilen profil, DNS ve reklam engelleme tercihleri yalnızca uygulamanın yerel UserDefaults alanında saklanır."
                )
                policySection(
                    "Blocklist sağlayıcısı",
                    "Reklam engelleme etkinleştirildiğinde uygulama, varsayılan olarak GitHub üzerindeki StevenBlack hosts listesini veya kullanıcının seçtiği HTTPS adresini indirir. Bu doğrudan isteğin standart ağ metadatası ilgili sunucu tarafından kendi gizlilik politikasına göre işlenebilir."
                )
                policySection(
                    "İletişim",
                    "Gizlilik ve destek talepleri için projenin herkese açık GitHub Issues sayfasını kullanabilirsiniz."
                )

                if let supportURL = URL(string: "https://github.com/Birkankader/GoodbyeAhmet-ios/issues") {
                    Link("Destek ve iletişim", destination: supportURL)
                }
            }
            .padding(24)
            .frame(maxWidth: .infinity, alignment: .leading)
        }
        .navigationTitle("Gizlilik")
        .navigationBarTitleDisplayMode(.inline)
    }

    private func policySection(_ title: String, _ text: String) -> some View {
        VStack(alignment: .leading, spacing: 6) {
            Text(title).font(.headline)
            Text(text).font(.body).foregroundColor(.secondary)
        }
    }
}
