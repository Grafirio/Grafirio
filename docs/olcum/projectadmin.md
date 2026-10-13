# ProjectAdmin: sistem metrikleri, loglar ve izler

ProjectAdmin'in başlangıç ekranı **Sistem metrikleri** (`/observability`) sayfasıdır.
Gateway, Identity API, Commerce API ve Data Analysis API'de zaten kurulu olan
OpenTelemetry hattındaki veriyi, ek bir toplayıcı gerektirmeden gösterir.
Semantik değerlendirme ve üretim sinyalleri mevcut **Semantik zekâ** ekranında kalır.

## Gösterilen veriler

- Mevcut OTel aboneliklerindeki bütün metrik adları: Grafirio LLM süre/token/maliyet,
  soru/sorgu/analiz/feedback, veri kaynağı/bridge, ASP.NET Core HTTP, HttpClient,
  .NET runtime/GC ve Npgsql tarafından yayımlanan ölçümler.
- Sayaç ve gauge değerleri; histogram örnek sayısı, toplamı, ortalaması, min/maks.
  Aynı adın farklı etiket kombinasyonları ayrı serilerdir. Hassas/bilinmeyen etiketler
  çıkarıldığı için bazı serilerin görünen etiketleri aynı olabilir; bunlar birleştirilmez.
- Süreç çalışma süresi, RSS, yönetilen heap, birikimli CPU saniyesi ve thread sayısı.
- Uygulama loglarının seviyesi, kategorisi, yapılandırılmış mesaj şablonu, hata türü,
  trace/span kimlikleri. Uygulamanın mevcut log seviye ayarları geçerlidir.
- Tamamlanmış ve OTel örnekleyicisinden geçmiş span kayıtları, süre, durum, güvenli
  etiketler ve parent ilişkisi. Trace ID'ye tıklamak servisler arasında log/iz filtreler.

## Saklama ve kapsam

Bu ekran **kalıcı log deposu veya tüm replikaların birleşik görünümü değildir**.
Her API isteği yönlendirildiği tek süreç örneğinin görüntüsünü döndürür. Örnek kimliği,
ortam ve toplama zamanı servis kartındadır. Load balancing nedeniyle sonraki
yenilemede başka bir örnek gelebilir; karşılaştırmadan önce örnek kimliğine bakın.

- Log ve iz için ayrı ayrı en fazla 1.000 kayıt, en fazla 30 dakikalık pencere.
- Zaman/seviye/trace filtresi **sonuç limiti uygulanmadan önce** çalışır.
  API varsayılan 100, en fazla 200 kayıt döndürür; panel 200 ister ve sayfalar.
- Metrik görünümü en fazla 1.000 seri; her 10 saniyede yenilenir. Gönderim öncesi
  SDK'nın kendi kardinalite sınırları da geçerlidir. Kapasite kayıpları panelde belirtilir.
- Sayaç/histogram değerleri süreç başlangıcından itibaren **kümülatif**; gauge son
  değerdir. 5/15/30 dakika filtresi yalnızca log/iz içindir, metriklere uygulanmaz.
- Yeniden başlatma geçmişi siler. İlk metrik toplaması için yaklaşık 10 saniye bekleyin.
- Erişilebilir kartı, telemetri endpoint'inin yanıt verdiğini belirtir; veritabanı veya
  bağımlılık sağlığı garantisi değildir. Servis arızaları ayrı gösterilir; diğer
  servislerin verisini engellemez ve arıza sıfır değer olarak sunulmaz.
- Docker stdout, işletim sistemi, Python/AI servisleri, Redis/Mongo/RabbitMQ/Keycloak
  logları bu OTel hattına dahil değildir. Bunların mevcut sağlık/kontrol ekranı
  **Servisler** sekmesidir; bunlar için yeni log toplayıcısı kurulmamıştır.

Mevcut OTLP/Aspire ve Azure Monitor aktarımı korunur. Uzun dönem saklama, tüm
replikalar, ham log ayrıntıları ve tam histogram kovaları için mevcut harici
gözlemlenebilirlik hedefini kullanın. Panel bu hedeflerin geçmişini sorgulamaz.

## Güvenlik ve API

Hem gateway rotaları hem doğrudan servis endpoint'leri Bearer kimlik doğrulaması ve
`business_roles=PLATFORM_ADMIN` ister. Anonim kullanıcı 401, firma yöneticisi 403 alır.
Yanıtlar `Cache-Control: no-store` taşır. Token tarayıcıdaki mevcut Keycloak istemcisinden gelir.

| Gateway GET yolu | Kaynak |
|---|---|
| `/admin/observability/gateway` | Gateway süreci |
| `/admin/observability/identity` | Identity `/api/admin/observability` |
| `/admin/observability/commerce` | Commerce `/api/admin/observability` |
| `/admin/observability/data-analysis` | Data Analysis `/api/admin/observability` |

Query parametreleri: `minutes=1..30`, `limit=1..200`, isteğe bağlı `level=Error`,
`traceId=<32-hex>`. Sayısal sınırlar sunucuda sıkıştırılır. Trace filtresi yalnızca log/iz içindir.

Log parametre değerleri, scope'lar, SQL/URL/query string/istek gövdesi ve exception
mesajı/stack trace bu panelin yanıtına kopyalanmaz. Şablonsuz logların mesajı gizlenir.
Bu, uygulamanın şablonun veya işlem adının içine doğrudan hassas veri yazmasını
güvenli hale getirmez: kaynak kodda hassas değerler daima yapılandırılmış parametre
olarak loglanmalı, sabit şablona birleştirilmemelidir. Dışa aktarıcıların davranışı değişmez.

## Yerel çalışma ve yayınlama

1. Dört .NET servisi ve gateway'i güncelleyip yeniden başlatın. Yeni DB migration,
   Express servis yöneticisi veya ek Docker servisi gerekmiyor.
2. ProjectAdmin'i `VITE_GATEWAY_URL=http://localhost:5000` ile derleyin/başlatın.
   Yerel geliştirme portu 59666 gateway'in CORS listesine dahildir.
3. Keycloak'ta `PLATFORM_ADMIN` claim'i olan hesapla giriş yapın ve
   `http://localhost:59666/observability` adresini açın.
4. Üretimde dört backend ve ProjectAdmin imajlarını birlikte yayınlayın. Mevcut
   reverse proxy cluster adresleri kullanılır; telemetriye özel yeni hedef URL yoktur.
5. Normal bir API isteği/analiz üretin, 10 saniye sonra metrikleri ve ilişkili log/izi
   kontrol edin. Eski servis imajı 404 döndürür; panel bunu açıkça gösterir.

## Doğrulama

```powershell
dotnet test tests/Grafirio.Gateway.Tests/Grafirio.Gateway.Tests.csproj
cd src/front/admins/grifirio.projectadmin
npm ci
npm run build
npx eslint src/pages/ObservabilityPage.jsx src/services/observabilityApi.js src/App.jsx
```

Testler gerçek OTel SDK üzerinden sayaç/histogram/gauge aktarımını, hassas alanların
çıkarılmasını, log/trace korelasyonunu, kapasite/zaman sınırlarını ve çalışan HTTP
endpoint'inin 401/403/200 ile cache davranışını doğrular.

## Ops ajanı: altyapı izleme ve kontrol

`src/front/admins/grifirio.projectadmin/api` artık eski Express servis yöneticisi değil,
**Docker'ın dışında, makinede** çalışan bir Ops ajanıdır. Docker kapandığında onunla birlikte
kapanmaması ve Docker Desktop'ı kendisinin başlatabilmesi için konteyner değildir; eski
`docker.sock` bağlantılı `service.manager.api` servisi ve komut satırı birleştiren uçlar kaldırıldı.

- **Servis kaydı:** [registry.json](../../src/front/admins/grifirio.projectadmin/api/registry.json).
  Tüm işlemler yalnızca buradaki servis kimlikleriyle yapılır; test, kaydın docker-compose'daki
  gerçek servis/konteyner adlarıyla uyumunu doğrular.
- **Sağlık:** Konteyner durumunun yanında gerçek yoklama: Postgres `SELECT 1`
  (`DATAANALYSIS_DB_PASSWORD` tanımlıysa), Mongo `ping`, Redis `PING`, HTTP ve TCP.
  Her servis için etki metni vardır (örn. "Kayıtlı bağlantı listesi açılmaz").
- **Kontrol:** başlat/durdur/yeniden başlat Docker Engine API'siyle yapılır (`exec` yok).
  Bilerek durdurulan servis otomatik onarılmaz.
- **Kendini onarma:** varsayılan kapalı. Açıkken art arda N başarısız yoklamadan sonra
  başlatır/yeniden başlatır; bağımlılığı düşükse önce onu bekler, bekleme süresi ve deneme
  sınırı vardır, sınır aşılırsa durur ve uyarı verir.
- **Denetim:** `api/data/audit.jsonl` (kim, ne zaman, neyi, sonuç). Ayarlar `api/data/settings.json`.
- **E-posta:** `OPS_SMTP_*` ve `OPS_ALERT_RECIPIENTS` doluysa yalnızca durum değişiminde
  (düştü / düzeldi / onarma vazgeçti) gönderilir; aynı olay 10 dakikada bir.

| Yol | Erişim |
|---|---|
| `GET /api/ops/status`, `POST /api/ops/services/:id/(start\|stop\|restart)`, `POST /api/ops/docker/start`, `POST /api/ops/infrastructure/up`, `GET\|PUT /api/ops/settings/autoheal`, `GET /api/ops/audit` | Keycloak token + `business_roles=PLATFORM_ADMIN` |
| `GET /api/ops/status/summary` | Anonim; yalnızca etki metinleri, servis adı/hata yok (kullanıcı kesinti banner'ı için) |
| `/recovery/` | Yalnızca `127.0.0.1` + `X-Recovery-Key`; yalnızca "Docker'ı başlat" ve "Altyapıyı kaldır". Token gerekmez, Docker/Keycloak kapalıyken kullanılır |

Kurulum (Docker Desktop'ı kullanan Windows kullanıcısıyla, yönetici olmadan):

```powershell
# .env içine OPS_RECOVERY_KEY yazın (örn. rastgele 32+ karakter), sonra:
.\scripts\Install-OpsAgent.ps1          # oturum açılışında otomatik başlayan zamanlanmış görev
.\scripts\Install-OpsAgent.ps1 -Uninstall
# elle: cd src/front/admins/grifirio.projectadmin/api; npm ci; npm start; npm test
```

Ajan varsayılan olarak yalnızca `127.0.0.1:3001` dinler. Gateway Docker içinde çalışıyorsa
(`host.docker.internal:3001` üzerinden ulaşır) `.env`'de `OPS_BIND_HOST=0.0.0.0` verin; token
zorunluluğu bu durumda da geçerlidir, `/recovery` ise yine yalnızca loopback'ten çalışır.

### Panel ve kullanıcı banner'ı

- **ProjectAdmin → Servisler** (`/services`): etki özeti, Docker/altyapı paneli, kategorili servis
  tablosu (durum, yanıt süresi, konteyner, hata, başlat/durdur/yeniden başlat — durdur ve yeniden
  başlat onay ister), kendini onarma ayarları, son uyarılar ve denetim kaydı. Durum 5 saniyede bir yenilenir.
- **Ajan adresi:** `VITE_OPS_AGENT_URL` (compose'da `http://localhost:3001`) verilirse panel ajana
  doğrudan gider; böylece Gateway düştüğünde de çalışır. Verilmezse Gateway'in `/ops/*` rotası
  kullanılır (`PlatformAdmin` politikası, `ops.cluster`). Yerel dev portu (59666) ajanın varsayılan
  CORS listesindedir; farklı bir adres için `OPS_ALLOWED_ORIGINS`.
- **Kullanıcı banner'ı:** UserAdmin, Gateway'in anonim `GET /ops/status/summary` rotasını 30 saniyede
  bir yoklar ve servis düşmüşse "Bazı hizmetler geçici olarak kullanılamıyor; kayıtlı verileriniz
  silinmedi" uyarısını etkilenen özelliklerle gösterir. İstek başarısız olursa banner gösterilmez
  (bilinmeyen durum, kesinti sayılmaz). Azure'da ajan yoktur; rota 502 döner ve banner çıkmaz.
