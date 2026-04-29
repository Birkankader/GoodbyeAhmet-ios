# GoodbyeAhmetWPF — İyileştirme Planı (TODO)

Bu belge, WPF projesinin (`GoodbyeAhmetWPF/`) kapsamlı bir kod analizi sonucunda tespit edilen **hataları**, **eksik özellikleri**, **güvenlik açıklarını** ve **kod kalitesi sorunlarını** önceliklendirilmiş olarak listeler. Her madde, hangi dosyada olduğunu, sorunun ne olduğunu ve önerilen çözümü içerir.

**Öncelik etiketleri:**
- 🔴 **HIGH** — Uygulamanın "bazen çalışmaması"nın muhtemel nedenleri. Hemen çözülmeli.
- 🟡 **MED** — Önemli, fakat hayati değil.
- 🟢 **LOW** — İyileştirme / nice-to-have.

> Toplam: **46 madde** (8 HIGH, 20 MED, 18 LOW)

---

## 🐞 1. HATALAR / DEFEKTLER (Bugs)

### 🔴 1.1 Mutex hiç serbest bırakılmıyor → "zaten çalışıyor" sahte uyarısı
- **Dosya:** [GoodbyeAhmetWPF/App.xaml.cs](GoodbyeAhmetWPF/App.xaml.cs)
- **Sorun:** `_mutex` `OnStartup`'ta oluşturuluyor, ama `OnExit`/`Dispose` çağrılmıyor. Uygulama beklenmedik şekilde kapanırsa (crash), sonraki açılışta "zaten çalışıyor" hatası verir veya yeni instance bloke olur.
- **Çözüm:** `OnExit` override ederek `_mutex?.ReleaseMutex(); _mutex?.Dispose();` çağır. Mutex adına versiyon/kullanıcı ID dahil et.

### 🔴 1.2 `GoodbyeDpiService` process handle sızıntısı
- **Dosya:** [GoodbyeAhmetWPF/Services/GoodbyeDpiService.cs](GoodbyeAhmetWPF/Services/GoodbyeDpiService.cs)
- **Sorun:** `Start()` arka arkaya çağrılırsa eski `_process` referansı dispose edilmeden üzerine yazılıyor. Ayrıca `IsRunning` cache'lenmiş `HasExited`'a güveniyor.
- **Çözüm:** Yeni process atamadan önce `_process?.Dispose()` çağır. `IsRunning`'i bir watchdog ile gerçek zamanlı kontrol et veya `Process.Exited` eventine abone ol.

### 🔴 1.3 `BoolToVisibilityConverter` — yanlış tip dönüşü
- **Dosya:** [GoodbyeAhmetWPF/SettingsWindow.xaml](GoodbyeAhmetWPF/SettingsWindow.xaml)
- **Sorun:** `IsEnabled` binding'inde `BoolToVisibilityConverter` kullanılıyor; converter `Visibility` döner ama `IsEnabled` `bool` bekler → silent binding error.
- **Çözüm:** `InverseBoolConverter` ekle veya `IsEnabled` için `Binding` mantığını düzelt.

### 🔴 1.4 Fire-and-forget `Task.Run` (yutulan istisnalar)
- **Dosya:** [GoodbyeAhmetWPF/ViewModels/MainViewModel.cs](GoodbyeAhmetWPF/ViewModels/MainViewModel.cs)
- **Sorun:** Constructor ve `SaveSettings` içindeki `_ = Task.Run(async () => { await EnsureLoadedAsync(); })` istisnaları yutuyor. Blocklist yüklenemezse kullanıcı bilgi alamıyor.
- **Çözüm:** Try-catch ekle, hatayı UI thread'e marshal et (MessageBox/StatusBar/Notification).

### 🔴 1.5 `NotificationService.SetDoubleClickAction` event birikimi
- **Dosya:** [GoodbyeAhmetWPF/Services/NotificationService.cs](GoodbyeAhmetWPF/Services/NotificationService.cs)
- **Sorun:** Her çağrıda yeni handler abone oluyor; eskiyi kaldırmıyor → çift tık çoklu kez tetikleniyor.
- **Çözüm:** Önceki handler'ı sakla ve `-=` ile kaldır, sonra `+=` yap.

### 🟡 1.6 Constructor içinde `Start()` patlarsa uygulama hiç açılmıyor
- **Dosya:** [GoodbyeAhmetWPF/ViewModels/MainViewModel.cs](GoodbyeAhmetWPF/ViewModels/MainViewModel.cs)
- **Sorun:** `ActivateOnStart=true` iken constructor içinde `Start(null)` çağrılıyor; admin yetkisi yoksa veya goodbyedpi.exe yoksa uygulama startup'ta crash eder.
- **Çözüm:** Try-catch ile sarmala; `Window.Loaded` eventi içinden çağır.

### 🟡 1.7 Ad-block kapatılınca blocklist bellekte kalıyor
- **Dosya:** [GoodbyeAhmetWPF/ViewModels/MainViewModel.cs](GoodbyeAhmetWPF/ViewModels/MainViewModel.cs), [GoodbyeAhmetWPF/Services/DnsBlocklistService.cs](GoodbyeAhmetWPF/Services/DnsBlocklistService.cs)
- **Sorun:** `AdBlockEnabled = false` yapılıp kaydedilse de `_blockedDomains` HashSet'i temizlenmiyor.
- **Çözüm:** `SaveSettings()` içinde `if (!_settings.AdBlockEnabled) _blocklistService.Clear();`.

### 🟡 1.8 Sayısal alanlar (TTL/Port) için validation yok
- **Dosya:** [GoodbyeAhmetWPF/SettingsWindow.xaml](GoodbyeAhmetWPF/SettingsWindow.xaml)
- **Sorun:** Kullanıcı "abc", "65536", negatif değer girebiliyor → goodbyedpi.exe sessizce başarısız oluyor (kullanıcı bunu "uygulama çalışmıyor" sanıyor).
- **Çözüm:** `PreviewTextInput` filtresi + `Save` öncesi `int.TryParse` & range kontrolü.

### 🟡 1.9 Preset hızlı değiştirme race condition
- **Dosya:** [GoodbyeAhmetWPF/ViewModels/MainViewModel.cs](GoodbyeAhmetWPF/ViewModels/MainViewModel.cs)
- **Sorun:** `SelectedPreset` setter'ı `ApplyPreset()` çağırıyor; hızlı switch'te kısmi state uygulanabilir.
- **Çözüm:** Re-entry guard flag veya `SemaphoreSlim`.

### 🟡 1.10 Window state / tray senkronizasyonu kırık
- **Dosya:** [GoodbyeAhmetWPF/MainWindow.xaml.cs](GoodbyeAhmetWPF/MainWindow.xaml.cs)
- **Sorun:** `LaunchOnStart=true` durumunda pencere hemen gizleniyor; tray ikonun görünür olduğunu garanti etmiyor → kullanıcı uygulamayı kaybediyor.
- **Çözüm:** Hide() öncesi tray icon `Visible=true` ve var olduğunu doğrula.

### 🟢 1.11 Mutex adı versiyondan bağımsız
- **Dosya:** [GoodbyeAhmetWPF/App.xaml.cs](GoodbyeAhmetWPF/App.xaml.cs)
- **Çözüm:** `$"GoodbyeAhmetWPF_{Version}_{userSid}"` formatı kullan.

### 🟢 1.12 `Directory.CreateDirectory` dönüş değeri kontrol edilmiyor
- **Dosya:** [GoodbyeAhmetWPF/Services/DnsBlocklistService.cs](GoodbyeAhmetWPF/Services/DnsBlocklistService.cs)
- **Çözüm:** Hata logla; kullanıcıya bildir.

### 🟢 1.13 `LocalizationService` eksik anahtar logu yok
- **Dosya:** [GoodbyeAhmetWPF/Services/LocalizationService.cs](GoodbyeAhmetWPF/Services/LocalizationService.cs)
- **Çözüm:** Eksik key için `Debug.WriteLine` veya log dosyasına yaz.

---

## ✨ 2. EKSİK ÖZELLİKLER / İYİLEŞTİRMELER

### 🔴 2.1 UAC / Admin manifest yok ⚠️ EN KRİTİK
- **Dosya:** [GoodbyeAhmetWPF/GoodbyeAhmetWPF.csproj](GoodbyeAhmetWPF/GoodbyeAhmetWPF.csproj)
- **Sorun:** GoodbyeDPI WinDivert sürücüsü kullanır → **admin yetkisi şart**. Manifest olmadığı için kullanıcı normal başlatınca goodbyedpi.exe sessizce hata veriyor → "uygulama bazen çalışmıyor" şikayetinin **ana nedeni budur.**
- **Çözüm:** `app.manifest` ekle, `<requestedExecutionLevel level="requireAdministrator" />` koy ve csproj'a `<ApplicationManifest>app.manifest</ApplicationManifest>` referansı ver.

### 🔴 2.2 Disk'e log yazımı yok
- **Dosya:** [GoodbyeAhmetWPF/App.xaml.cs](GoodbyeAhmetWPF/App.xaml.cs)
- **Sorun:** Sadece `MessageBox` ve `Trace.WriteLine`. Kullanıcı sorun bildiremiyor.
- **Çözüm:** `%APPDATA%\GoodbyeAhmet\logs\` altında rolling log (Serilog/NLog veya basit FileLogger). Tüm exception'lar, process stdout/stderr'i logla.

### 🔴 2.3 Network parametre validasyonu (IP/port/TTL)
- **Dosya:** [GoodbyeAhmetWPF/SettingsWindow.xaml](GoodbyeAhmetWPF/SettingsWindow.xaml)
- **Çözüm:** `IPAddress.TryParse`, port 1-65535, TTL 0-255 kontrolü.

### 🔴 2.4 Blocklist URL HTTPS zorunluluğu yok
- **Dosya:** [GoodbyeAhmetWPF/Services/DnsBlocklistService.cs](GoodbyeAhmetWPF/Services/DnsBlocklistService.cs)
- **Çözüm:** HTTP URL reddet veya en azından kullanıcıyı uyar.

### 🔴 2.5 GoodbyeDPI binary integrity check yok
- **Dosya:** [GoodbyeAhmetWPF/Services/GoodbyeDpiService.cs](GoodbyeAhmetWPF/Services/GoodbyeDpiService.cs)
- **Çözüm:** Bilinen SHA256 hash listesini csproj'a embed et; başlatmadan önce doğrula.

### 🟡 2.6 Process crash watchdog yok
- **Dosya:** [GoodbyeAhmetWPF/Services/GoodbyeDpiService.cs](GoodbyeAhmetWPF/Services/GoodbyeDpiService.cs)
- **Çözüm:** `Process.Exited` event'i + `EnableRaisingEvents=true`. Beklenmedik exit'te kullanıcıyı uyar / otomatik yeniden başlat.

### 🟡 2.7 Firewall kuralı yönetimi
- **Çözüm:** Açılışta `netsh advfirewall` ile inbound/outbound kural kontrolü; eksikse ekle.

### 🟡 2.8 Preset import/export
- **Dosya:** [GoodbyeAhmetWPF/Services/PresetService.cs](GoodbyeAhmetWPF/Services/PresetService.cs)
- **Çözüm:** Preset'leri JSON olarak `%APPDATA%`'ya kaydet; import/export butonları.

### 🟡 2.9 Blocklist boyut limiti yok
- **Dosya:** [GoodbyeAhmetWPF/Services/DnsBlocklistService.cs](GoodbyeAhmetWPF/Services/DnsBlocklistService.cs)
- **Çözüm:** Max ~50 MB; aşılırsa reddet (DoS koruması).

### 🟡 2.10 Dil değişimi otomatik kaydedilmiyor
- **Dosya:** [GoodbyeAhmetWPF/ViewModels/MainViewModel.cs](GoodbyeAhmetWPF/ViewModels/MainViewModel.cs)
- **Çözüm:** Dil değişiminde anında persist et veya kullanıcıyı uyar.

### 🟡 2.11 Hata mesajları lokalize değil
- **Dosya:** Birçok yer (Services & ViewModels)
- **Çözüm:** Tüm string'leri `local/*.json` dosyalarına taşı.

### 🟡 2.12 Settings backup/export
- **Çözüm:** Ayarlar penceresine "Export/Import Settings" butonları.

### 🟢 2.13 Update checker
- **Çözüm:** GitHub Releases API'den versiyon kontrolü (HTTPS, timeout'lu).

### 🟢 2.14 Sistem proxy desteği
- **Dosya:** [GoodbyeAhmetWPF/Services/DnsBlocklistService.cs](GoodbyeAhmetWPF/Services/DnsBlocklistService.cs)
- **Çözüm:** `HttpClientHandler { UseProxy = true, Proxy = WebRequest.DefaultWebProxy }`.

### 🟢 2.15 Light/Dark theme switching
- **Dosya:** [GoodbyeAhmetWPF/Resources/DarkTheme.xaml](GoodbyeAhmetWPF/Resources/DarkTheme.xaml)
- **Çözüm:** ResourceDictionary swap mantığı + ayarlardan seçim.

### 🟢 2.16 Blocklist cache temizleme butonu
- **Çözüm:** Settings'e "Clear Cache" butonu.

---

## 🔒 3. GÜVENLİK AÇIKLARI

### 🔴 3.1 Process argument injection riski
- **Dosya:** [GoodbyeAhmetWPF/Services/GoodbyeDpiService.cs](GoodbyeAhmetWPF/Services/GoodbyeDpiService.cs)
- **Sorun:** `Modeset`, `TTL`, `Fragment` gibi alanlar kullanıcı input'u olarak doğrudan komut satırına ekleniyor. Kötü niyetli config dosyası argüman enjekte edebilir.
- **Çözüm:** Whitelist regex (`^-?\d+$`) ile her parametreyi doğrula. `ProcessStartInfo.ArgumentList` kullan (string concat yerine).

### 🟡 3.2 JSON deserialization size/schema validation yok
- **Dosya:** [GoodbyeAhmetWPF/Services/SettingsService.cs](GoodbyeAhmetWPF/Services/SettingsService.cs)
- **Çözüm:** Dosya boyut limiti + try-catch + default fallback.

### 🟡 3.3 HTTPS sertifika doğrulaması — pinning yok
- **Dosya:** [GoodbyeAhmetWPF/Services/DnsBlocklistService.cs](GoodbyeAhmetWPF/Services/DnsBlocklistService.cs)
- **Çözüm:** GitHub raw için sertifika pin'leme veya en azından TLS 1.2+ zorunlu.

### 🟡 3.4 Blocklist içerik doğrulaması yok
- **Çözüm:** Hash doğrulaması veya HMAC ile imzalı blocklist.

### 🟡 3.5 Process startup timeout yok
- **Dosya:** [GoodbyeAhmetWPF/Services/GoodbyeDpiService.cs](GoodbyeAhmetWPF/Services/GoodbyeDpiService.cs)
- **Çözüm:** Async start + cancellation token (10 sn).

### 🟢 3.6 Exception detayları UI'a sızıyor
- **Dosya:** [GoodbyeAhmetWPF/ViewModels/MainViewModel.cs](GoodbyeAhmetWPF/ViewModels/MainViewModel.cs)
- **Çözüm:** Generic mesaj göster, full detay log'a yaz.

### 🟢 3.7 Plaintext blocklist cache
- **Çözüm:** DPAPI ile şifrele veya `%APPDATA%` (kullanıcıya özel) altına taşı.

### 🟢 3.8 settings.json çok kullanıcılı senaryoda paylaşımlı
- **Dosya:** [GoodbyeAhmetWPF/Services/SettingsService.cs](GoodbyeAhmetWPF/Services/SettingsService.cs)
- **Çözüm:** `%APPDATA%\GoodbyeAhmet\settings.json` kullan; Program Files içinde tutma.

---

## 🧹 4. KOD KALİTESİ

### 🔴 4.1 MVVM ihlali — View içinde business logic
- **Dosya:** [GoodbyeAhmetWPF/MainWindow.xaml.cs](GoodbyeAhmetWPF/MainWindow.xaml.cs)
- **Çözüm:** Tray ve window state mantığını ViewModel'a (veya behavior'a) taşı.

### 🟡 4.2 Async void / fire-and-forget
- **Dosya:** [GoodbyeAhmetWPF/ViewModels/MainViewModel.cs](GoodbyeAhmetWPF/ViewModels/MainViewModel.cs)
- **Çözüm:** `async Task` + `await` + try/catch.

### 🟡 4.3 `IDisposable` zinciri uygulanmamış
- **Dosyalar:** `MainViewModel`, `GoodbyeDpiService`, `DnsBlocklistService`, `NotificationService`
- **Çözüm:** `IDisposable` implement et; `MainWindow.Closed` içinde çağır.

### 🟡 4.4 `DnsBlocklistService` thread safety
- **Dosya:** [GoodbyeAhmetWPF/Services/DnsBlocklistService.cs](GoodbyeAhmetWPF/Services/DnsBlocklistService.cs)
- **Çözüm:** `volatile HashSet` yetersiz; `ImmutableHashSet` veya `lock` kullan.

### 🟡 4.5 CancellationToken desteği yok
- **Çözüm:** Network indirmelerde CT propagation.

### 🟡 4.6 `RelayCommand` weak event eksikliği
- **Dosya:** [GoodbyeAhmetWPF/ViewModels/RelayCommand.cs](GoodbyeAhmetWPF/ViewModels/RelayCommand.cs)
- **Çözüm:** `WeakEventManager<CommandManager>` veya kendi weak listener'ın.

### 🟡 4.7 Settings reset deep copy sorunu
- **Çözüm:** Yeni `SettingsFile` instance ile değiştir.

### 🟢 4.8 String comparison tutarsızlığı (kültür/case)
- **Çözüm:** Her yerde `OrdinalIgnoreCase`.

### 🟢 4.9 Magic number'lar (timeout, balloon süresi vb.)
- **Çözüm:** `AppConstants` static class.

### 🟢 4.10 Null-coalescing eksikleri
- **Dosya:** [GoodbyeAhmetWPF/Services/LocalizationService.cs](GoodbyeAhmetWPF/Services/LocalizationService.cs)
- **Çözüm:** `?? new List<...>()` defansif kod.

### 🟢 4.11 Event handler unsubscribe disiplini
- **Çözüm:** `Closed` event'inde kaldır.

### 🟢 4.12 Merkezi konfigürasyon yok
- **Çözüm:** `AppConfig` veya `appsettings.json`.

---

## 🚀 ÖNERİLEN UYGULAMA SIRASI (Sprint Planı)

### Sprint 1 — "Bazen çalışmıyor" sorununu kökten çöz (HIGH) ✅ TAMAMLANDI
1. ✅ **2.1** UAC/Admin manifest ekle ⚠️
2. ✅ **2.2** Disk'e log yazımı (Serilog veya basit FileLogger)
3. ✅ **1.1** Mutex temizliği (`OnExit` + dispose)
4. ✅ **1.6** Constructor `Start()` try/catch + `Loaded`'a taşı
5. ✅ **1.4** Fire-and-forget Task'lara try/catch
6. ✅ **1.2** Process handle dispose + watchdog
7. ✅ **2.6** `Process.Exited` event ile crash detection
- 🎁 Bonus: **1.5** Notification handler birikimi düzeltildi
- 🎁 Bonus: **1.10** Tray ikonu Hide() öncesi `EnsureVisible()` ile garanti
- 🎁 Bonus: **3.1** Argument injection koruması (whitelist regex + IP/port/TTL validation)
- 🎁 Bonus: **1.11** Mutex adına versiyon + kullanıcı SID dahil edildi

### Sprint 2 — Veri bütünlüğü ve güvenlik (HIGH) ✅ TAMAMLANDI
8. ✅ **2.3** Network parametre validasyonu (UI + service katmanı)
9. ✅ **2.4** Blocklist HTTPS zorunluluğu + 50 MB indirme limiti
10. ✅ **3.1** Argument injection koruması (Sprint 1'de yapıldı, `InputValidator`'a refactor edildi)
11. ✅ **2.5** GoodbyeDPI binary hash check (`hashes.txt` opsiyonel — fail-open)
12. ✅ **3.8** Settings dosyasını `%LOCALAPPDATA%`'ya taşı (legacy auto-migration + atomic write)
- 🎁 Bonus: **3.5** Process startup timeout / size limit (50 MB streaming + http timeout 30s)
- 🎁 Bonus: **3.2** JSON deserialization size limit (1 MB cap)
- 🎁 Bonus: TextBox'larda numeric-only PreviewTextInput + paste filter

### Sprint 3 — UX iyileştirmeleri (MED) ✅ TAMAMLANDI
13. ✅ **1.3** BoolToVisibility binding hatası → yeni `InverseBoolConverter`
14. ✅ **1.5** Notification handler birikimi (Sprint 1'de yapıldı)
15. ✅ **1.7** Ad-block kapatınca blocklist temizle (Sprint 1'de yapıldı)
16. ✅ **1.8** Sayısal input filtresi (Sprint 2'de yapıldı)
17. ✅ **2.10** Dil değişimi anında kaydet
18. ✅ **2.11** Hata mesajları lokalize (`InvalidSettings`, `ValidationXxx`, `ErrorStartingGoodbyeDpi` vb.)
19. ✅ **1.9** Preset hızlı değişim race condition (`_applyingPreset` guard)
- 🎁 Bonus: **1.13** LocalizationService'e en-US fallback + missing-key uyarısı (`Logger.Warn`)

### Sprint 4 — Kod kalitesi & ekstra özellikler (MED/LOW) ✅ TAMAMLANDI
19. ✅ **4.3** `IDisposable` zinciri (`MainViewModel`, `GoodbyeDpiService`)
20. ✅ **2.6** Crash watchdog → otomatik restart (üstel backoff: 2/4/8 sn, 2 dk pencerede max 3 deneme)
21. ✅ **2.8** Preset import/export (`PresetImportExportService`, `%LOCALAPPDATA%/.../presets.json`, OpenFile/SaveFile dialog)
22. ✅ **2.13** Update checker (`UpdateService` → GitHub Releases API, opt-out)
23. ✅ **2.14** Sistem proxy desteği (`HttpClientFactory` → `WebRequest.GetSystemWebProxy()` + `DefaultCredentials`)
24. ✅ **2.16** Blocklist cache temizleme butonu (`DnsBlocklistService.ClearCache()`)
- 🎁 Bonus: "Open Log Folder" butonu (kullanıcı log'ları kolay paylaşsın diye)
- 🎁 Bonus: Preset sanitization (geçersiz IP/port/TTL otomatik temizleniyor)
- 🎁 Bonus: 256 KB import limit (DoS koruması)

### Atlanan maddeler (gelecek sprintlere bırakıldı)
- **2.7** Firewall kuralı yönetimi — `netsh advfirewall` entegrasyonu kompleks; manuel test gerekiyor
- **2.15** Light/Dark theme switching — büyük UI revizyonu; ayrı sprint hak ediyor
- **3.3** HTTPS sertifika pinning — GitHub raw için kırılgan; CI'de hash rotation gerekir
- **4.6** RelayCommand weak event — mevcut binding ölçeğinde kritik değil

---

## NOTLAR
- **"Bazen çalışmıyor" semptomu** büyük ihtimalle: (a) admin manifest eksik, (b) constructor'da silent exception, (c) GoodbyeDPI process'i admin olmadan başlatılıp sessizce ölüyor, (d) mutex artığı, kombinasyonundan kaynaklanıyor.
- Sprint 1 bu maddelerin tamamını adresliyor — önce bunlar bitirilmeli, sonra plan yeniden değerlendirilmeli.

---

_Rapor tarihi: 29 Nisan 2026_
