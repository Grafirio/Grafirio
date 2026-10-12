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
