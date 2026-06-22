# GoodbyeAhmet Gizlilik Politikasi

Yururluk tarihi: 19 Haziran 2026

GoodbyeAhmet, kullanici gizliligini temel alarak tasarlanmis yerel bir Packet Tunnel uygulamasidir.

## Veri toplama

GoodbyeAhmet; kimlik, konum, cihaz tanimlayicisi, tarama gecmisi, DNS sorgusu veya paket icerigi toplamaz. Uygulamada hesap sistemi, analitik, reklam ya da izleme SDK'si yoktur.

## VPN trafigi

Ag trafigi, hizmeti saglamak amaciyla yalnizca cihazdaki Packet Tunnel Extension icinde gecici olarak islenir ve hedef sunucuya dogrudan iletilir. Gelistirici tarafindan isletilen bir proxy veya VPN sunucusuna gonderilmez. Trafik kaydedilmez, satilmaz, baska amacla kullanilmaz ve ucuncu taraflara aciklanmaz.

GoodbyeAhmet genel IP adresini degistiren bir uzak VPN hizmeti degildir. TLS bolme ve DNS yonlendirme islemlerini cihazda gerceklestirir.

## Yerel ayarlar

Secilen profil, DNS sunucusu ve reklam engelleme tercihleri yalnizca uygulamanin yerel UserDefaults alaninda saklanir.

## Blocklist saglayicisi

Reklam engelleme etkinlestirildiginde uygulama, varsayilan olarak GitHub uzerindeki StevenBlack hosts listesini veya kullanicinin girdigi HTTPS adresini indirir. Bu dogrudan istegin IP adresi gibi standart ag metadatasi, ilgili sunucu tarafindan kendi gizlilik politikasina gore islenebilir.

## Iletisim

Gizlilik ve destek talepleri icin [GitHub Issues](https://github.com/Birkankader/GoodbyeAhmet-ios/issues) sayfasini kullanabilirsiniz.
