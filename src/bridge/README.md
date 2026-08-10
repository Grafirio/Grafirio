# Grafirio Bridge

Müşterinin kendi ağında çalışan, veritabanına bulut yerine **içeriden** bağlanan
servis.

## Neden var

Bulut, müşterinin SQL Server'ına dışarıdan içeri TCP açamıyor: kurumsal
veritabanları firewall/NAT arkasında, laptop'taki bir veritabanına ise
buluttan hiç ulaşılamıyor.

Bridge bunu yönü çevirerek çözüyor: **bağlantıyı müşterinin sunucusu kurar**,
giden 443/WSS ile. Müşterinin firewall'ında hiçbir giriş portu açılmaz. Aynı
prensip Postman Desktop Agent, Power BI On-premises Data Gateway ve Azure
Self-hosted Integration Runtime'da da kullanılıyor.

## Müşterinin BT ekibine verilen cevaplar

| Soru | Cevap |
| --- | --- |
| Hangi port açılacak? | Hiçbiri. Yalnızca **giden** 443 gerekiyor. |
| Bağlantıyı kim kurar? | Sizin sunucunuz. Bulut size hiç bağlanmaz. |
| Şifrem nerede duruyor? | Sizin diskinizde, DPAPI ile makineye bağlı olarak. Bulutta kalıcı kopyası yok. |
| Buluttan gelen sorgu verimi değiştirebilir mi? | Hayır. Bridge yalnızca `SELECT`/`WITH` kabul eder, kontrolü **sizin makinenizde** yapar. |
| Hangi tablolara erişilir? | İzin listesi tanımlarsanız yalnızca onlara; tanımlamazsanız bağlantının gördüğü tablolara. |
| Ne yapıldığını nereden görürüm? | `audit.tsv` — sizin diskinizde, bizim erişimimiz olmadan. |

## Kurulum

1. Grafirio panelinde **Bridge ekle** → tek kullanımlık kayıt token'ı alın
   (2 saat geçerli).
2. `appsettings.json` dosyasını doldurun:

```json
{
  "Bridge": {
    "ServerUrl": "https://api.grafirio.com",
    "EnrollmentToken": "panelden-aldiginiz-token",
    "Name": "Merkez SQL Sunucusu"
  }
}
```

3. Servisi kurun:

```powershell
sc.exe create "Grafirio Bridge" binPath= "C:\Program Files\Grafirio\Bridge\GrafirioBridge.exe" start= auto
```

4. Kayıt tamamlandıktan sonra `EnrollmentToken` alanını silebilirsiniz; token
   zaten harcanmıştır.

## Veritabanı bağlantılarının tanımlanması

Bağlantı bilgileri **yerel** durum dosyasında tutulur
(`C:\ProgramData\Grafirio\Bridge\state.dat`, DPAPI ile şifreli). En katı
kurulumda şifre panele hiç girilmez; dosyaya müşteri kendi yazar ve panel
yalnızca bağlantının adını görür.

İzin listesi (`allowedTables`) doldurulursa, listede olmayan bir tabloya giden
sorgu bridge tarafından reddedilir — bulut ne gönderirse göndersin.

## Sınırlar

- **Tek replika.** Sunucu tarafındaki istek eşleştirmesi süreç içi çalışıyor;
  bridge bir replikaya bağlanıp sorgu başka replikaya düşerse açık bir hata
  döner (sessizce askıda kalmaz). `data-analysis-api` çok replikaya çıkmadan
  önce cevap kanalının Redis üzerinden taşınması gerekiyor.
- **Windows dışında DPAPI yok.** Linux'ta durum dosyası şifrelenmeden yazılır
  ve açılışta uyarı verilir; dosya izinlerini kendiniz kısıtlamanız gerekir.
