# Azure ortam yapılandırması

`appsettings.json` ve `docker-compose.yml` yerel geliştirmeyi hedefler; Azure'da farklı
olan her şey Container Apps üzerinde **environment variable** ve **ingress** ayarı olarak
tutulur. Bu dosya o farkları kayıt altına alır — çoğu, servisler aylarca ayağa kalkmadığı
için fark edilmemiş hatalardı.

## Ingress: iç servisler düz HTTP kabul etmeli

Gateway (`Grafirio.Gateway`) YARP cluster adreslerini `http://…internal…` olarak tanımlar.
Container Apps ingress'i `allowInsecure=false` iken bu isteklere **301 ile kendi iç HTTPS
adresine** yönlendirme döner; YARP bu yönlendirmeyi istemciye aktarır ve tarayıcı çözemediği
bir hostname'e gider. Sonuç: gateway üzerinden geçen her çağrı 404.

```bash
az containerapp ingress update -g grifirio-rg -n identity-api --allow-insecure
az containerapp ingress update -g grifirio-rg -n commerce-api --allow-insecure
az containerapp ingress update -g grifirio-rg -n data-analysis-api --allow-insecure
```

## Ingress: veritabanları TCP transport ister

Postgres, MongoDB, SQL Server ve RabbitMQ ham TCP protokolü konuşur. Ingress `transport=Auto`
(yani HTTP) iken bağlantılar kurulamaz. `postgres-keycloak` baştan `Tcp` olduğu için Keycloak
çalışıyordu, diğerleri çalışmıyordu.

```bash
az containerapp ingress update -g grifirio-rg -n postgres-dataanalysis --transport tcp
az containerapp ingress update -g grifirio-rg -n mongo-catalog        --transport tcp
az containerapp ingress update -g grifirio-rg -n sqlserver            --transport tcp
az containerapp ingress update -g grifirio-rg -n rabbitmq             --transport tcp
az containerapp ingress update -g grifirio-rg -n redis                --transport tcp
```

## Servis adları compose'dan farklı

Compose `nokta.ayrilmis.isimler` kullanır, Azure Container Apps ise `tire-ayrilmis` isimler.
`appsettings.json` compose isimlerini taşıdığı için Azure'da DNS çözülmez
("Name or service not known").

| docker-compose | Azure Container App |
| --- | --- |
| `postgres.db.dataanalysis` | `postgres-dataanalysis` |
| `postgres.db.keycloak` | `postgres-keycloak` |
| `mongo.db` | `mongo-catalog` |
| `sqlserver.db.order` | `sqlserver` |
| `rabbitmq.container` | `rabbitmq` |
| `redis.db.basket` | `redis` |

## Container app başına gereken env değişkenleri

**data-analysis-api**

```
ConnectionStrings__DefaultConnection=secretref:dataanalysis-db-connection
RabbitMQ__Host=rabbitmq
Redis__ConnectionString=redis:6379,password=…,abortConnect=false
PyCaret__BaseUrl=http://pycaret-engine
```

**commerce-api** — `appsettings.json` yalnızca `appsettings.Development.json` içinde
`IdentityOption` tanımlıyordu; Production ortamında bu dosya yüklenmediği için servis
"IdentityOption configuration is missing" ile açılışta ölüyordu. Artık `appsettings.json`
içinde, ayrıca Redis bağlantısı env'den geliyor:

```
ConnectionStrings__Redis=redis:6379,password=…,abortConnect=false
```

**identity-api** — Keycloak admin bilgileri (repoya yazılmaz):

```
KeycloakAdmin__Realm=grafirio
KeycloakAdmin__Username=…
KeycloakAdmin__Password=…
```

## Frontend build arg'ları

Vite değişkenleri **build sırasında** gömülür; container'a sonradan env vermek işe yaramaz.
Kod `VITE_API_URL` okur — Dockerfile bir dönem yalnızca `VITE_API_GATEWAY_URL` tanımladığı
için bundle `http://localhost:5000`'e düşüyor ve tarayıcı API çağrılarını kullanıcının kendi
makinesine gönderiyordu. Varsayılanlar `src/front/grifirio.front/Dockerfile` içinde tutulur.

Hâlâ eksik: `VITE_DJANGO_AI_URL`, `VITE_DJANGO_AI_WS_URL`, `VITE_SCHEMA_ANALYZER_URL`,
`VITE_PYCARET_ENGINE_URL`. Bu servisler internal ingress'te ve gateway'de karşılık gelen bir
route yok, dolayısıyla tarayıcıdan erişilemiyorlar. Kullanılacaklarsa önce gateway'e route
eklenmeli.

## Ölçüm ve izleme (2026-09-29)

### Application Insights — telemetri

`grafirio-insights` (workspace-based, `workspace-grifiriorgEPdI`'a bağlı; günlük alım tavanı
**1 GB**). Servisler iz, metrik ve logu `Grafirio.Telemetry` üzerinden **doğrudan** gönderiyor
(Azure Monitor OpenTelemetry Exporter). Container Apps'in yönetilen OTel ajanı kullanılmıyor:
App Insights'a metrik göndermiyor, oysa LLM token ve maliyet sayaçları metrik.

identity-api, commerce-api, data-analysis-api ve gateway'de:

```
APPLICATIONINSIGHTS_CONNECTION_STRING=secretref:appinsights-connection
```

Bağlantı dizesi yeni kod deploy edilene kadar kullanılmıyor (eski imajlar okumaz; zararsız).

### LLM maliyeti (data-analysis-api)

`grafirio-llm` deployment'ı **gpt-5-mini** (GlobalStandard). Maliyet hesabı için (USD / 1M token,
OpenAI liste fiyatı — sözleşme farklıysa güncelleyin):

```
Llm__Pricing__InputPerMillion=0.25
Llm__Pricing__CachedInputPerMillion=0.025
Llm__Pricing__OutputPerMillion=2.00
```

### Benchmark dashboard — değerlendirme koşuları

Admin paneldeki Semantik zekâ kartlarının kaynağı (ayrı depo: `benchmarkt`).

| Kaynak | Değer |
|---|---|
| Container App | `benchmark-dashboard` — **internal** ingress, `allowInsecure`, system-assigned managed identity |
| İmaj | `grifirioacr.azurecr.io/benchmark-dashboard:<commit>` — `az acr build` ile (CI'da değil) |
| Veritabanı | Azure SQL `grafirio-bench-swedencentral` / `benchmarks` — serverless, **ücretsiz teklif**, limit dolunca AutoPause |
| Kimlik | SQL **yalnızca Entra** (parola yok). Dashboard kendi managed identity'siyle bağlanıyor; veritabanında `db_ddladmin`, `db_datareader`, `db_datawriter` |
| Güvenlik duvarı | `AllowAzureServices` (Container Apps'in giden IP'si sabit değil) |
| Dışarıdan erişim | Yalnızca gateway: `/benchmark/{**}` → `PlatformAdmin` politikası (token'da `business_roles` ∋ `PLATFORM_ADMIN`) |

SQL sunucusu West/North Europe'ta açılamadı ("not accepting creation of new … servers"),
Sweden Central'da.

Env değişkenleri:

```
Database__Provider=SqlServer
ConnectionStrings__Benchmarks=Server=tcp:grafirio-bench-swedencentral.database.windows.net,1433;Database=benchmarks;Authentication=Active Directory Managed Identity;Encrypt=True;Connect Timeout=60
Ingest__ApiKey=secretref:ingest-api-key
```

Güncelleme:

```bash
az acr build -r grifirioacr -t benchmark-dashboard:<commit> <benchmarkt klasörü>
az containerapp update -n benchmark-dashboard -g grifirio-rg --image grifirioacr.azurecr.io/benchmark-dashboard:<commit>
```

Ölçüm araçlarıyla Azure'daki dashboard'a gönderim gateway üzerinden:
`--publish https://gateway…/benchmark`, `MEASURE_DASHBOARD_TOKEN=<PLATFORM_ADMIN token>`,
`MEASURE_DASHBOARD_KEY=<Ingest:ApiKey>` (anahtar: `az containerapp secret show -n benchmark-dashboard -g grifirio-rg --secret-name ingest-api-key`).

## Kalıcılık

Veritabanı **container'larında** (`postgres-*`, `mongo-catalog`, `sqlserver`, `redis`, `rabbitmq`)
kalıcı disk yoktur: Azure Files SMB, veritabanı motorlarının ihtiyaç duyduğu POSIX kilitlemeyi
desteklemediği için mount edilemiyor. Platform bakımında replikalar yeniden oluşur ve içindeki
**tüm veri silinir** (13 Ekim 2026 00:23 UTC'de olan buydu). Kalıcı veri bu container'larda tutulmamalı.

Kalıcı veri yönetilen servislerde:

| Veri | Yer |
| --- | --- |
| Keycloak (kullanıcılar, client'lar, bridge kimlikleri) | Azure Database for PostgreSQL `grifirio-keycloak-db`, veritabanı `keycloak_db` |
| Kayıtlı bağlantılar, analizler, sorgu geçmişi (data-analysis-api) | Aynı sunucu, veritabanı `grafirio_dataanalysis`, rol `dataanalysis_user` (yalnızca bu veritabanının sahibi) |
| Bridge yönlendirmeleri, tablo seçimleri | Cosmos DB for MongoDB (vCore) `grafirio-mongo` (`mongo-connection` secret'ı) |

`grifirio-keycloak-db`: Burstable B1ms, PostgreSQL 16, 35 gün otomatik yedek (coğrafi yedek ve HA kapalı).
data-analysis-api bağlantı dizesi `dataanalysis-db-connection` secret'ından gelir
(`ConnectionStrings__DefaultConnection=secretref:dataanalysis-db-connection`); parola repoda yoktur.
`postgres-dataanalysis` ve `mongo-catalog` container'ları artık kullanılmıyor; doğrulamadan sonra silinebilir.

Yeni bir veritabanı eklerken: Azure PostgreSQL'de yeni oluşturulan veritabanının `public` şeması
uygulama rolüne ait olmaz ve `EnsureCreated()` `42501: permission denied for schema public` ile
tabloları kuramaz. Veritabanını rol adına oluşturup ardından `ALTER SCHEMA public OWNER TO <rol>`
çalıştırın. Güvenlik duvarı: `AllowAllAzureServices` kuralı container'ların erişimini sağlar;
yönetim için geçici bir IP kuralı ekleyip işiniz bitince silin.

Şemanın kendini kurması:

- Keycloak: `start --import-realm` + [keycloak/realm-grafirio.json](../keycloak/realm-grafirio.json)
- data-analysis-api: açılışta `EnsureCreated()` çağırır; hata olursa loglar (`Database initialization failed`)
  ama servis ayakta kalır, bu yüzden bağlantı/yetki düzeldikten sonra servisi yeniden başlatmak gerekir.
