# Localhost ve IP bağlantısı: uygulama ve operasyon kaydı

Tarih: 11 Ekim 2026. Bu belge canlı yapılandırma düzeltmeleri ile henüz
yayımlanmamış uygulama kodunu birbirinden ayırır.

## Doğrulanan kök nedenler ve canlı düzeltmeler

1. Keycloak `grafirio` realm'inde backend'in kullandığı `bridge-provisioner`
   istemcisi eksikti. `client_not_found` nedeniyle yönetim token isteği 401,
   Bridge kayıt isteği 502 dönüyordu. Mevcut backend secret'ı değiştirilmeden
   confidential service-account istemcisi oluşturuldu; yalnızca
   `realm-management/manage-clients` rolü verildi. Tarayıcı ve parola grant'leri
   açılmadı. Token ve geçici istemci oluştur/oku/devre dışı bırak/sil kontrolü geçti.
2. Mevcut masaüstü uygulaması 11:51:09–10 civarında Bridge kaydını tamamladı
   ve buluta bağlandı. Yeni uygulama paketi yüklenmeden bu sonuç gözlendi.
3. Yerel `SQLEXPRESS` çalışıyordu fakat TCP kapalıydı. Registry yedeği alındı,
   TCP yalnızca `127.0.0.1:1433` ve `[::1]:1433` üzerinde açıldı ve SQL servisi
   yeniden başlatıldı. LAN/firewall erişimi, SQL parolaları ve kimlik doğrulama
   modu değiştirilmedi.

Yerel geri alma yedeği:
`C:\Users\fthsu\AppData\Local\Temp\Grafirio-SqlTcp-20261011-115300.reg`.
Geri alma yalnızca bu makinede, yönetici onayı ve SQL kesinti penceresiyle
`reg.exe import` ve `Restart-Service 'MSSQL$SQLEXPRESS'` kullanılarak yapılmalıdır.
Yedek yalnızca SQL TCP registry ayarlarını kapsar.

**Doğrulanmayan:** Kullanıcının SQL hesabıyla oturum açma ve sorgu çalıştırma.
Windows Integrated Authentication denemesi `Cannot generate SSPI context`
verdi; bu, formdaki SQL kullanıcı adı/parolasının yanlış olduğunu göstermez.
Parola alınmadı, okunmadı veya değiştirilmedi. Açık TCP portu tek başına
başarılı SQL bağlantısı anlamına gelmez.

## Bağlantı sözleşmesi

| Kullanım | SQL bağlantısını açan | Localhost'un anlamı |
| --- | --- | --- |
| Masaüstü | Oturumdaki cihazın native Bridge'i | Kullanıcının bu bilgisayarı |
| Web + Bridge | Açıkça seçilen şirkete ait Bridge | Seçilen Bridge'in bilgisayarı |
| Web + doğrudan | Grafirio backend'i | Bulut sunucusu; kullanıcı bilgisayarı değil |

Masaüstü localhost, IPv4/IPv6, LAN/VPN IP veya sunucu adı girebilir; gerçek
erişilebilirlik cihazın ağına, SQL TCP dinleyicisine, porta, TLS'e ve SQL
yetkilerine bağlıdır. Web'de doğrudan yol yalnızca backend'in erişebildiği
hedefler içindir. Başarısız Bridge bağlantısı otomatik olarak doğrudan yola
veya başka cihaza geçirilmez. Tarayıcı güvenliği/CORS kapatılmaz; tarayıcıdan
SQL TCP bağlantısı açılması beklenmez.

## Kod değişiklikleri

- Backend, Bridge ve önyüzde tutarlı adres/port ayrıştırması; çelişen port,
  URL, instance adı ve geçersiz IPv6 reddi. SQL instance'ı için keşif yerine
  açık TCP portu kullanılır.
- Masaüstü kayıt hatası native kanaldan güvenli `code`, `reason`, `retryable`
  ile iletilir. Kalıcı yapılandırma hatasında arka plan yeni kayıt isteği
  göndermez; açık kullanıcı denemesi veya oturumu durdurma toparlanmaya izin verir.
- Web formunda doğrudan/Bridge ve cihaz seçimi; test sırasında kayıt yapılmaz.
  Masaüstünde farklı kayıtlı hedefi bu cihaza taşıma parola ve onay gerektirir.
- Bozuk yönlendirme kaydı doğrudan yola düşmez. Katalog bağımlılığı hatası
  `CATALOG_UNAVAILABLE`/503 ve destek koduyla ayrıştırılır.
- SQL hataları parola/connection-string ayrıntısı yerine güvenli DNS/port,
  kimlik doğrulama, veritabanı ve TLS açıklamalarıyla gösterilir.
- Doğrudan bulut erişiminde DNS yanıtları denetlenir ve seçilen IP sabitlenir;
  TLS sertifika doğrulaması için asıl hostname korunur. Loopback, metadata,
  link-local ve altyapı adresleri reddedilir. Özel ağ hedefleri ancak açık
  `DirectConnections:AllowedPrivateHosts` yapılandırmasıyla izinli olur.
  Bu ayar ağ bağlantısı sağlamaz; VNet/VPN ve ağ seviyesinde egress kontrolü
  ayrıca gereklidir. Bridge'in kullanıcı ağına erişimi bu bulut kuralından ayrıdır.
- Redis varsa Bridge adres defteri 90 saniyelik lease kullanır; 30 saniyelik
  heartbeat yeniler. Eski soket yeni lease'i silemez veya yenileyemez. Dizin
  erişilemiyorsa yerel kayda güvenilmez. Redis yoksa tek replika zorunludur.
- Bridge yalnızca WebSocket kullanır ve negotiation adımını atlar. Eski
  istemciler için rollout boyunca session affinity veya tek replika gerekir.
  Ortak dizin, SignalR Redis backplane ve cevap otobüsü birlikte kullanılmalıdır.
- `/health/live` süreç canlılığı; `/health/ready` ve `/health` PostgreSQL/Mongo
  hazırlığı içindir. Bağımlılık kontrollerine ortak 5 saniyelik iptal bütçesi
  verilir. LLM yapılandırması ayrıca raporlanır; yokluğu SQL bağlantı yolunu
  sağlıksız saydırmaz. Gerçek LLM çağrısı sağlık denetiminde yapılmaz.
- Üretim önyüz derlemesi HTTPS, localhost olmayan `VITE_API_URL` ister.
  CI, backend/önyüz dağıtımından önce provisioner için salt-okunur kontrol yapar.

## Güvenli operasyon komutları

Repo kökünde, Azure hesabı gerekli kaynak grubuna yetkiliyken:

```powershell
# Salt-okunur varsayılan: secret değerleri çıktı olarak yazılmaz.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Repair-BridgeProvisioner.ps1

# Yalnızca onaylı kimlik servisi onarımı; geçici istemci testi de mutasyon yapar.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Repair-BridgeProvisioner.ps1 -Repair -SmokeTest

# Yerel SQL TCP durumunu okur; yönetici yetkisiyle -Apply değişiklik yapar.
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\scripts\Enable-LocalSqlTcp.ps1
```

Bu makinede betik yürütme varsayılan olarak kapalı olduğundan `ExecutionPolicy`
yalnızca başlatılan süreç için belirtilmiştir; makinenin kalıcı politikası
değiştirilmez. Komutlar yalnızca incelenmiş repo betikleri için kullanılmalıdır.

[Provisioner betiği](../scripts/Repair-BridgeProvisioner.ps1) mevcut secret'ı
yeniden kullanır; beklenmedik hedef, mevcut istemci ayarı veya secret uyuşmazlığında
işlemi durdurur. Bu betik Grafirio'nun tanımlı üretim realm/uygulama isimlerine
özeldir. CI'da yalnızca doğrulama çalışır, otomatik onarım yapılmaz.
[SQL betiği](../scripts/Enable-LocalSqlTcp.ps1) bu makinedeki SQLEXPRESS/1433
onarımı içindir; bütün kullanıcıların SQL yapılandırmasını otomatik değiştirmez.
TCP zaten açıksa veya port kullanımda ise zorla yeniden yapılandırmaz.

## Doğrulama ve kontrollü yayın

Yerelde backend **752**, Bridge **159**, masaüstü **100**, önyüz **87** test
geçti: toplam **1.098**, başarısız veya atlanan test yok. Önyüz üretim derlemesi
başarılı. Değişen önyüz dosyalarında lint hatası yok; mevcut `loadConnections`
effect bağımlılığı uyarısı devam ediyor. Son salt-okunur kontrolde provisioner
token/istemci sorgusu ve iki loopback SQL dinleyicisi tekrar doğrulandı.
Bu testler gerçek SQL parolası, gerçek Redis kümesi veya installer
kurulumunun yerine geçmez. Mevcut bağımlılık güvenlik uyarıları ve büyük önyüz
paketi uyarısı bu bağlantı değişikliğinin dışındadır.

Yayın sırası ve kabul ölçütleri:

1. Staging'de provisioner salt-okunur kontrolü ve geçici istemci yaşam döngüsü.
2. Backend + Redis: aynı sürüm iki replika; Bridge A'ya bağlıyken B'den test,
   sorgu, satır aktarımı ve iptal. Tenant sınırı, Redis kesintisi, TTL aşımı,
   eski/yeni soket yarışı ve replica yeniden başlatma. Kesintide direct fallback yok.
3. Eski backend ve yeni backend aynı lease sözleşmesini paylaşmadığından
   karışık sürümlü çok-replika trafik testi tamamlanmadan yaygın dağıtım yapılmaz.
   İlk rollout kontrollü tek replika/affinity ile yapılır; yeni backend sağlıklı
   olduktan sonra yeni önyüz ve masaüstü paketi yayımlanır, sonra ölçek artırılır.
4. Temiz Windows kurulumu ve mevcut sürümden yükseltme: imzalı installer,
   WebView2, oturum açma/çıkma, enrollment ve yeniden başlatma kontrolü.
5. SQL kabul testi: masaüstünde `localhost` ve port `1433`, kullanıcı tarafından
   uygulama içinde girilen SQL parolasıyla **Test Et → Kaydet → Tablo seçimi →
   sınırlı okuma sorgusu**. Test Et sırasında kayıt oluşmamalıdır. Ek olarak
   `127.0.0.1`, `::1`, erişilebilir LAN/VPN ve uzaktaki SQL test edilir.
6. Web: doğrudan erişilebilir SQL IP/hostname; seçili Bridge üzerinden yerel SQL;
   çevrimdışı Bridge, yanlış parola, kapalı port, TLS hatası ve bozuk katalog.
   Hata doğru katmanı göstermeli; başka hedefe sessiz geçiş olmamalıdır.
7. Hazırlık/liveness probe yollarını platformda ayırın; Bridge iş yükü için
   en az bir sıcak replika planlayın. Secret, parola ve bearer token loglamayın.

**Yayın durumu:** Bu çalışma yeni kaynak kodunu commit/push etmedi, container
veya masaüstü installer yayımlamadı. Canlıya uygulananlar yalnızca yukarıdaki
Keycloak ve yerel SQL yapılandırma düzeltmeleridir. Gerçek çok-replika ve
kullanıcı SQL hesabı kabul testleri tamamlanmadan tam uçtan uca başarı ilan edilmez.
