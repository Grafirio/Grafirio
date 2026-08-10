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

## Çok replikalı çalışma

Bridge tek bir replikaya bağlanıyor. Sorgu isteği başka bir replikada doğduysa
iki ayrı şey gerekiyor ve ikisi de Redis'e bağlı:

1. **İstek bridge'e ulaşsın** — SignalR'ın Redis backplane'i.
2. **Cevap sorguyu başlatan replikaya dönsün** — backplane bunu taşımaz,
   yalnızca sunucudan istemciye gideni taşır. Bu yüzden ayrı bir cevap
   otobüsü var (`IBridgeResponseBus`).

Sahiplik sunucu tarafında tutuluyor: `requestId → instanceId` Redis'e yazılıyor
ve cevap hangi replikaya düşerse düşsün oradan sahibine yönlendiriliyor.
**Protokol değişmiyor — bridge hangi replikanın beklediğini bilmiyor.** Bunu
bridge'e söylemek, müşteri makinesindeki bir yazılımı bizim ölçeklendirme
kararlarımıza bağımlı kılardı.

Devreye almak için `ConnectionStrings__Redis` tanımlamak yeterli. Tanımsızsa
süreç içi uygulama kullanılıyor ve servis tek replika varsayımıyla çalışır.

## Sınırlar

- **Windows dışında DPAPI yok.** Linux'ta durum dosyası şifrelenmeden yazılır
  ve açılışta uyarı verilir; dosya izinlerini kendiniz kısıtlamanız gerekir.
- **Redis yolu canlıda henüz koşmadı.** Yönlendirme mantığı iki replikayı
  taklit eden testlerle doğrulandı; gerçek Redis'e karşı bir koşum yapılmadı.
