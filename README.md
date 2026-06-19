# GoodbyeAhmet iOS

SwiftUI ile yazilmis native iOS DPI bypass uygulamasi. Gomulu Packet Tunnel Extension, TLS ClientHello paketlerini boler, DNS yonlendirmesi yapar ve istege bagli hosts tabanli reklam engelleme uygular.

## Xcode'da acma

1. `GoodbyeAhmet.xcodeproj` dosyasini Xcode 26.3 veya daha yeni bir surumle ac.
2. `GoodbyeAhmet` scheme'ini ve iPhone'unu sec.
3. Telefonun kilidini acip Run dugmesine bas.
4. Ilk baglantida iOS'un VPN yapilandirmasi ekleme iznini onayla.

Network Extension yetkili provisioning profilleri `project.yml` icinde tanimlidir. Projeyi yeniden uretmek gerekirse `xcodegen generate` calistirilabilir.

## Yapi

- `GoodbyeAhmet/`: SwiftUI uygulamasi, ayarlar ve VPN yoneticisi
- `TunnelExtension/`: Packet Tunnel, TCP/UDP aktarimi, DPI bypass ve adblock
- `GoodbyeAhmet.xcodeproj/`: Dogrudan Xcode'da acilabilir proje
