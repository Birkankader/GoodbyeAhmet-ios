# App Store Gonderim Kontrol Listesi

## Apple Developer

- Uygulama, Apple App Review Guideline 5.4 geregi bir organizasyon adina kayitli Apple Developer hesabi ile incelemeye gonderilmelidir. Kisisel `7K4V3H32VN` takimi teknik olarak IPA uretebilse de VPN uygulamasini App Store'da sunmaya uygun degildir.
- `com.birkankader.dpi` ve `com.birkankader.dpi.extension` kimliklerinde Network Extension / Packet Tunnel yetkisi acik olmalidir.
- Release arsivi icin App Store dagitim sertifikasi ve iki hedefe ait dagitim provisioning profilleri Xcode tarafindan otomatik yonetilmelidir.

### Mevcut imzalama durumu

- Kisisel `7K4V3H32VN` takiminin Apple Development ve Apple Distribution sertifikalari gecerli.
- Iki App ID icin Network Extension / Packet Tunnel yetkili gelistirme ve App Store profilleri mevcut.
- Imzali Release arsivi ve App Store Connect IPA export islemi basarili.
- Ana uygulama ve tunnel extension, `Apple Distribution: Birkan Kader (7K4V3H32VN)` ile imzali; gomulu Store profilleri ve derin imza dogrulamasi gecerli.
- Teknik imzalama hazir olsa da kisisel hesapla App Review'a gonderim Guideline 5.4 ile uyusmaz. Incelemeye gondermeden once hesabi organizasyona donusturun veya uygulamayi uygun bir organizasyon hesabina transfer edin.

## App Store Connect

- Privacy Policy URL: `https://github.com/Birkankader/GoodbyeAhmet-ios/blob/main/PRIVACY.md` (bu dosya `main` dalina alindiktan sonra).
- Support URL: `https://github.com/Birkankader/GoodbyeAhmet-ios/issues`
- App Privacy yanitlarini mevcut kodla uyumlu olacak sekilde "Data Not Collected" olarak doldurun. Yeni analitik, reklam veya uzak sunucu eklenirse bu beyan yeniden incelenmelidir.
- Export Compliance sorularini App Store Connect'te yanitlayin. Uygulama HTTPS ve Apple ag API'lerini kullandigi icin hukuki muafiyet karari gelistirici hesabinda dogrulanmadan `ITSAppUsesNonExemptEncryption` anahtari eklenmemistir.
- VPN uygulamasi icin secilen ulkelerde gereken lisanslari kontrol edin; lisans gerektiren bir bolgede belge yoksa o bolgeyi dagitimdan cikarin.
- 1024x1024 App Store ikonu, iPhone/iPad ekran goruntuleri, kategori, yas derecelendirmesi ve destek iletisimini tamamlayin.

## App Review Notu

GoodbyeAhmet uses Apple's Network Extension packet-tunnel API. All packet processing happens locally on the device. The app does not connect to a developer-operated VPN or proxy server, does not change the user's public IP address, and does not collect, retain, sell, use, or disclose browsing traffic. Before the first connection, the app presents a clear VPN data disclosure and requires affirmative consent.

Review steps: launch the app, tap the main connection button, accept the VPN data disclosure, approve the iOS VPN configuration prompt, and wait for the status to become Connected. Settings includes the full privacy policy and optional HTTPS blocklist configuration.

## Arsiv

1. `xcodegen generate` calistirin.
2. Xcode'da `GoodbyeAhmet` scheme, `Release` ve `Any iOS Device (arm64)` secin.
3. `Product > Archive` ile arsiv olusturun.
4. Organizer icindeki `Validate App` tamamlanmadan yukleme yapmayin.
