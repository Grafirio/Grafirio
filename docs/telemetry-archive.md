# Kalıcı telemetri arşivi

Bu uygulama mevcut OpenTelemetry snapshot üreticilerini genişletir; sorgu yürütme,
`IDataSourceFactory` ve QueryPolicy sınırlarını değiştirmez. Arşiv, müşteri veritabanından
ayrı bir yönetim deposudur. Azure üretim arşivi ayrı yönetilen PostgreSQL veritabanını kullanır;
yerel kurulum için SQLite seçeneği korunur.

## Akış ve kimlik sınırları

1. .NET snapshot'larından güvenli log metadatası, tamamlanmış trace'ler ve metrik örnekleri
   sınırlı arka plan kuyruğuyla toplayıcıya gönderilir. Canlı snapshot ve OTLP resource aynı instance kimliğini kullanır.
   Mevcut OTLP/Application Insights çıkışları korunur; arşiv onların saklama garantisini değiştirmez.
2. Python aynı kayıt sözleşmesine güvenli bir logging handler ve HTTP middleware üzerinden yazar.
   HTTP trace bağlamı taşınır. Kuyruktaki analiz işlerinin ilişkisi query_id ile kurulur;
   kaydedilmemiş tarihsel span ilişkileri üretilmez. Veri portu dönüşündeki sorgu kimliği .NET'te
   mevcut iç servis anahtarı ve firma/bağlantı/iş kapsamı doğrulandıktan sonra etiketlenir.
3. Her .NET servisi ve Python için farklı, en az 32 karakterlik rastgele bir üretici anahtarı gerekir.
   Bir anahtar yalnızca tanımlı servise kayıt yazabilir. Data Analysis, doğruladığı Bridge kayıtlarını da iletebilir.
   Üretici anahtarları okuma/yönetim yetkisi vermez; platform admin JWT'si de üretici anahtarı yerine geçmez.
4. Bridge mevcut Keycloak/SignalR kanalıyla küçük bir outbox gönderir. Sözleşmede firma, trace veya
   takip kimliği alanı yoktur. Hub firma/Bridge kimliğini token'dan belirler, mevcut kayıt defterindeki
   şirket ve aktif socket eşleşmesini doğrular. Kullanıcı oturumuna veya Python iç servis kimliğine dönüşmez.
5. Bridge request_id, sunucunun oluşturduğu `bridge.dispatch` kaydındaki aynı firma + Bridge + request_id
   ile eşleşiyorsa sorgu/trace görünümüne bağlanır. Edge'in bildirdiği rastgele bir istek kimliği başka
   firmanın sorgusuna bağlantı kuramaz. Query ile diagnostic ilişkisini yalnızca sunucunun
   `query.correlation` kayıtları kurar. UUID biçimleri standartlaştırılır.

## Depolama ve bütçe

Node **24** gerekir. Yerel kurulumda Node'un yerleşik
[SQLite API'si](https://nodejs.org/download/release/latest-v24.x/docs/api/sqlite.html) kullanılır.
Tek bir toplayıcı yazarı kalıcı yerel disk üzerindeki `telemetry/telemetry.sqlite` dosyasına yazar.
Çok sayıda kaynak replika desteklenir; toplayıcıyı aynı dosyaya yazan birden fazla replika olarak çoğaltmayın.
Bu dosyayı SMB/NFS üzerinden ortak veritabanı yapmayın. SQLite kilitlerini ve fsync garantisini destekleyen
kalıcı yerel/blok disk gerekir; Container Apps'in geçici dosya sistemi veya doğrulanmamış ağ paylaşımı yeterli değildir.
Bu dağıtım koşulu sağlanamıyorsa canlıya çıkmadan depo adaptörü yönetilen bir veritabanına taşınmalıdır.

### Azure PostgreSQL kurulumu (18 Ekim 2026)

Kullanıcının seçimiyle mevcut `grifirio-keycloak-db` sunucusunda ayrı `grafirio_telemetry`
veritabanı ve aynı adlı kısıtlı uygulama rolü hazırlandı. Keycloak/analiz veritabanlarının
tabloları veya verileri değiştirilmedi. Mevcut `pg` bağımlılığı kullanılır; PostgreSQL
bağlantısında sertifika doğrulaması açıktır. Şema kurulum aracınca oluşturulur; uygulama
açılışı DDL çalıştırmaz. Uygulama rolünün şema oluşturma/tablo silme yetkisi yoktur;
`audit` tablosunda yalnızca INSERT/SELECT vardır. Saklama temizliği audit kayıtlarına dokunmaz.
Audit ayrıca izlenmeli ve işletim politikasına göre arşivlenmelidir.

`telemetry-archive` Container App aynı ortamda **internal ingress + HTTPS zorunlu** olarak
çalışır; veritabanı ve üretici anahtarları Azure Container Apps secret referanslarıdır.
`TELEMETRY_DATABASE_URL` varsa toplayıcı PostgreSQL kullanır, yoksa yerel SQLite kullanır.
Container Apps'in geçici diskine arşiv veya audit yazılmaz. Collector 0,25 CPU / 0,5 GiB,
min/max 1 replika ile kurulur. Şema/denetim/olaylar mevcut yönetilen sunucunun yedekleme
politikasına dahildir; PostgreSQL için SQLite'ın 128 MiB dosya sınırı geçerli değildir.
Runtime havuzu örnek başına en fazla 3 bağlantıdır; uygulama rolü geçişte eski/yeni
revision ve doğrulamayı karşılayacak şekilde toplam 8 bağlantıyla sınırlıdır.
7 gün / 50.000 olay / 64 MiB olay gövdesi politikası her iki depoda aynıdır; PostgreSQL
indeksleri, boş sayfalar, audit ve sunucudaki diğer veriler bu gövde sınırına dahil değildir.

PostgreSQL adaptörü aynı zarf normalizasyonu ve sorgu/Bridge korelasyon planını SQLite
ile paylaşır. Batch transaction commit'inden sonra onaylanır; veritabanı advisory transaction
kilidi birden fazla revision/replica sırasında yazma, temizlik ve ayarları sıraya koyar.
Kalıcı PostgreSQL audit'e istek kaydı yazılamıyorsa saklama ayarı kabul edilmez.

İşletim aracı: `node tools/telemetry/configure-azure.cjs` (`database`, `collector`,
`producer <app>`, `panel`, `verify`). `verify` arşivi kısıtlı uygulama rolüyle okur,
şema/izin değişikliği yapmaz. Planlı yeniden başlatma kontrolünde önce `capture`, ardından
toplayıcı güncellemesi/yeniden başlatması ve hemen sonra `check-persistence` kullanılır.
Yakalanan sınır saklama politikası nedeniyle zamanla silinebilir; eski sınır rutin sağlık
kontrolü değildir. Bu araç uygulama/CI tarafından otomatik çağrılmaz.
Gerçek üretim kaynağına yazar; değişiklik kapsamı ve imajlar incelenmeden çalıştırılmamalıdır.
Sırlar konsola veya komut argümanına yazılmaz. Yerel kurtarma kopyası Git dışında
`.worktree/telemetry-cloud-secrets.dpapi` dosyasında Windows kullanıcı kimliğiyle şifrelenir;
dosyayı veya Windows profilini kaybetmek mevcut Azure secret'larını kendiliğinden döndürmez.
Kurulum için açılan geçici yönetim IP kuralı `finally` içinde kaldırılır; süreç zorla
sonlandırılırsa `telemetry-setup-*` kuralları elle kontrol edilmelidir.

Kaynak imajları `tools/telemetry/prepare-build-context.cjs` ile Git dışındaki salt kaynak
kopyasından derlenir; `.env*`, çalışma verileri, bağımlılık klasörleri ve test çıktıları
yüklenmez. .NET imajları `shared-packages.ref` içindeki aynı commit'e sabitlenir.

- Başlangıç politikası: en fazla **7 gün, 50.000 kayıt, 64 MiB normalize olay gövdesi**.
  İlk dolan sınır belirleyicidir; yoğun trafikte 7 günlük geçmiş garantisi yoktur.
- Panelden 1–30 gün, 100–100.000 kayıt ve 1–64 MiB gövde sınırı ayarlanabilir.
  Bu rakamlar test edilmiş kapasite hedefi veya bulut maliyet tahmini değildir.
- SQLite dosyasına 128 MiB sayfa sınırı uygulanır. İndeksler, rollback journal ve audit dosyası
  gövde boyutuna dahil değildir. Pilot için en az 512 MiB disk bütçesi ve disk/audit izleme ayırın.
  Audit mevcut ekleme yöntemini kullanır; bağımsız rotasyon/yedekleme işletim sorumluluğudur.
- İsteğe bağlı yerel compose toplayıcısı 256 MiB bellek ve 0,5 CPU ile sınırlandırılır;
  konteyner logu 2 × 5 MiB döndürülür. Sürekli yük/performans ölçümü yapılmamıştır.
- Temizlik her dakika, kayıt kabulünde ve geçmiş sorgusunda çalışır. Arşiv dışındaki tabloları/dosyaları temizlemez.
  Süre ve kapasite nedeniyle çıkarılan kayıt sayıları ayrı görünür.
- Aynı service + instance + event ID tekrar geldiğinde yeniden sayılmaz. Batch kabulü SQLite transaction
  commit'inden sonradır. Geçersiz batch kısmen yazılmaz.
  Kaynaklar HTTP başarı durumuna ek olarak accepted/inserted/duplicates sayılarının batch ile eşleşmesini
  doğrular; HTML fallback veya ilgisiz bir 200 yanıtı Bridge outbox'ını boşaltmaz.
- Tarih aralığı en fazla 30 gün; sayfa başına en fazla 200 kayıt. Sayfalar ilk yanıtın sequence üst sınırını
  korur. Arada saklama temizliği yapılırsa eski sayfalardan kayıt eksilebilir; bu tam veritabanı snapshot'ı değildir.

## Kesinti davranışı

.NET ve Python kuyrukları 2.000 kayıtla sınırlı ve bellektedir; yeniden başlatmada henüz gönderilmeyenler kaybolabilir.
Gönderim zaman aşımı 5 saniyedir; başarısızlıkta aynı event ID ile yeniden denenir.
Geçersiz/çok büyük batch kuyruk başını sonsuza kadar tıkamamak için düşürülür ve sayılır.
Sorgu yürütme toplayıcı cevabını beklemez.

Bridge outbox'ı mevcut Bridge veri klasöründe `telemetry-outbox.json` olarak atomik dosya değiştirmeyle saklanır:
en fazla 1.000 olay / 7 gün; gönderimler 50 olaylıktır. Yeniden bağlanınca tekrar gönderilir.
Kayıtlar yalnızca kalıcı commit onayı sonrasında outbox'tan çıkarılır; kaybolan onay yeniden gönderime yol açar,
arşiv bunu tekilleştirir. Yeniden kayıtla Bridge sahibi değişirse eski sahibin outbox'ı gönderilmez.
Disk hatası sorguyu durdurmaz fakat yerel kalıcılık kaybolabilir. Yerel dosyaya SQL/mesaj/kimlik sırrı yazılmaz.
Eski sunuculara yeni metot çağrılmaması için sunucu `TelemetryAvailable` yeteneğini bildirir.

Arşiv ekranı son alınan kaynak/instance ve kaynakta düşen kayıtları gösterir. Uzun süre veri alınmaması
tek başına servis kapalı demek değildir. .NET canlı ekranında kuyruk, düşen kayıt ve başarısız aktarım sayıları da bulunur.
Python sadece olay/log oluştuğunda gönderir; ayrı bir sağlık heartbeat'i olarak yorumlanmamalıdır.

## Gizlilik ve yönetim

Arşiv sözleşmesi mesaj gövdesi, SQL, sonuç, log parametreleri, exception mesajı, HTTP URL'si veya serbest
attributes alanı içermez. Teknik kategori, exception türü, durum, kimlikler ve sayısal metrikler kalır.
Metrik etiketleri iki tarafta izin listesiyle daraltılır; replica/zaman örnekleri toplanarak işlem sayısı veya P95 üretilmez.
Kaynak filtreleme üzerine toplayıcıda tekrar alan projeksiyonu uygulanır.

Okuma ve ayarlar sunucuda `PLATFORM_ADMIN` gerektirir; yanıtlar `no-store` döner.
Saklama politikası değişikliği panelde kalıcı silme etkisiyle onaylanır, yönetici + önce/sonra + sonuç audit'e yazılır.
Bu ikinci kişi onayı değildir. Üretimde toplayıcıya erişim TLS üzerinden olmalı; anahtarlar secret yönetiminden sağlanmalıdır.
Yalnızca yerel compose ağında açık HTTP kullanmak için ayrıca opt-in bulunur; üretimde bu seçeneği açmayın.

## Yapılandırma ve kurulum hazırlığı

Yerel compose adımları isteğe bağlıdır; otomatik üretim dağıtımı eklenmez.

Toplayıcı girişi: `src/front/admins/grifirio.projectadmin/api/telemetry-server.js`.
Dockerfile: aynı klasörde `Dockerfile.telemetry`. Bu giriş Docker socket'i veya servis kontrol/recovery API'si açmaz.
Mevcut host Ops ajanı da isteğe bağlı `TELEMETRY_ARCHIVE_ENABLED=true` ile aynı route/depo uygulamasını kullanabilir.

| Taraf | Ayarlar |
| --- | --- |
| Toplayıcı | `OPS_DATA_DIR`, `OPS_BIND_HOST`, `OPS_PORT`, `OPS_ALLOWED_ORIGINS`, `OPS_OIDC_ISSUER`; gerekiyorsa ayrı iç ağ `OPS_OIDC_JWKS_URL` |
| Azure arşiv deposu | `TELEMETRY_DATABASE_URL` = ayrı PostgreSQL veritabanı URL'si (secret); TLS sertifikası doğrulanır |
| Üretici anahtarları | `TELEMETRY_GATEWAY_KEY`, `TELEMETRY_IDENTITY_KEY`, `TELEMETRY_COMMERCE_KEY`, `TELEMETRY_DATA_ANALYSIS_KEY`, `TELEMETRY_PYTHON_KEY` |
| Her .NET servis | `TelemetryArchive__Endpoint` = tam `/api/telemetry/ingest` HTTPS adresi; `TelemetryArchive__Key` = o servisin anahtarı |
| Python | `TELEMETRY_ARCHIVE_ENDPOINT`, `TELEMETRY_ARCHIVE_KEY`; metadata için `ENVIRONMENT`, `GRAFIRIO_BUILD_SHA` |
| Gateway | `ReverseProxy__Clusters__telemetry.cluster__Destinations__destination1__Address` = toplayıcının iç ağ adresi |
| Panel | Varsayılan gateway `/admin/telemetry`; gerekirse `VITE_TELEMETRY_ARCHIVE_URL` tam HTTPS `/api/telemetry` adresi |

Gateway geçişi sadece platform yöneticisine açıktır; üreticiler gateway'in yönetici rotasını kullanmaz,
doğrudan iç toplayıcıya gider. JWKS için iç adres verilmesi JWT issuer kontrolünü değiştirmez.
Servis anahtarları eksik/kısa ise kaynak aktarımı açılmaz. Toplayıcı girişi kısa veya yinelenen üretici anahtarlarını reddeder.

Yerel overlay: `docker-compose.telemetry.yml`, ana compose dosyasıyla **aynı mevcut yığın** üzerinde kullanılır.
Ana compose'un zorunlu ortam değişkenlerine ek olarak beş farklı üretici anahtarı gerekir.
Sadece yapılandırma kontrolü: `docker compose -f docker-compose.yml -f docker-compose.telemetry.yml --profile telemetry config --quiet`.
Gerçek anahtarları komut satırına, kaynak dosyaya veya rapora yazmayın. İkinci bir worktree'de tam yığını başlatmayın.
Toplayıcı servisi `telemetry` profili seçilmeden açılmaz; yayın portu yalnızca `127.0.0.1:3002`'dir.

## Doğrulama

- Node sentetik testleri: yeniden açılma, replica ayrımı, tekilleştirme, maskeleme, geçersiz batch atomikliği,
  kimlik/yönetici ayrımı, yanlış firma/Bridge ilişkisinin dışlanması, saklama/hacim sınırı ve sayfalama.
- PostgreSQL entegrasyonu: ayrı rastgele sentetik şema/rol, eşzamanlı tekrar gönderim,
  yeniden bağlanma, bulk batch transaction, aynı ortak sorgu filtreleri/Bridge izolasyonu,
  async HTTP hataları ve kalıcı audit; runtime rolünün audit silme/tablo silme/DDL reddi.
  `TEST_TELEMETRY_POSTGRES_URL` yalnızca loopback `GrafirioEval_*` test veritabanını kabul eder;
  değişken verilmezse bu entegrasyon testi atlanır. Üretim URL'sini test girdisi yapmayın.
- .NET: kolektör kesintisinde aynı ID ile yeniden gönderim; Bridge kalıcı outbox, sahip değişimi ve disk hatası;
  hub'da yanlış firma/eski socket reddi ve arşiv kapalıyken onay verilmemesi.
- Python: mesaj gövdesinin çıkmaması, kuyruk sınırı, HTTP korelasyonu, kapalı aktarım ve yeniden deneme;
  mevcut analiz ve iç servis güvenlik testleri.
- Tarayıcı: takip kimliğiyle arşiv açma, filtre/sayfa sınırını koruma, kesintiyi hata olarak gösterme ve
  politika değişikliğinin onay öncesinde gönderilmemesi. Veriler sentetiktir; dış origin istekleri engellenir.

### Canlı kurulum doğrulaması

18 Ekim 2026 kurulumu: toplayıcı `admin-20261018-pg2`; gateway, identity-api,
commerce-api, data-analysis-api, pycaret-engine ve projectadmin `admin-20261018-pg1`
imajlarıyla hazır revision durumuna geçti. Mevcut TLS alan adları kullanıldı.

- Python konteynerinden toplayıcı iç HTTPS sağlık kontrolü 200; manager ve gateway
  sağlık kontrolü 200; gateway arşiv status/events oturumsuz istekleri 401.
- Dört .NET servisi ve Python kaynağı kalıcı depoda görüldü. Python doğrulamasında
  kimlik doğrulamalı kök durum GET'i kullanıldı; analiz/model/müşteri sorgusu çalıştırılmadı.
  Python yayını öncesi mevcut iş veritabanında aktif/bekleyen iş sayısı 0 görüldü.
- Toplayıcı revision değişiminden önce yakalanan snapshot/zaman aralığındaki aynı olay,
  yeni toplayıcı hazır olduktan sonra tekrar okundu. Arşiv kısıtlı rolünün audit DELETE
  ve şema CREATE yetkilerinin kapalı olduğu üretimde de kontrol edildi.
- İlk PostgreSQL sürümündeki olay başına yazma, ağ gecikmesinde aktarımı yavaşlattı.
  pg2 her batch'i iki toplu INSERT ile aynı transaction içinde yazar. Revision geçişindeki
  bağlantı limitine de pay eklendi. İlk kurulum sırasında kaynak tamponlarında toplam
  **1.857 telemetri olayı** düştü: gateway 1.077, data-analysis 609, identity 122,
  commerce 49; Python 0. Bu sayaçlar sıfırlanmadı. 21:44:41 ve 21:45:30 UTC civarındaki
  iki ölçümde sayaçlar değişmedi; kayıt sayısı 16.233'ten 17.740'a yükseldi.
  Bu kısa gözlem sürekli yük veya kayıpsız teslim garantisi değildir; düşmüş olaylar
  bu kurulumda geri doldurulmadı.
- Geçici yönetim IP kuralları kaldırıldı; gerçek anahtarlar rapora/kaynağa yazılmadı.
  Bridge outbox'ı için müşteri istemcilerinin yeni sürüme geçmesi gerekir; bu işlem
  Bridge yükleyicisi yayınlamadı ve müşteri cihazlarını güncellemedi.

Üretim yönetici oturumuyla tarayıcıda tüm akışların yürütülmesi, gerçek çoklu host/depo
kesintisi, uzun süreli yük ve maliyet ölçümü bu kontrollerle doğrulanmış sayılmaz.
Konteyner stdout/stderr toplama ve yüksek erişilebilir arşiv deposu ayrıca kapsamlandırılmalıdır.
