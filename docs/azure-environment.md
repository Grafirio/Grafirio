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
ConnectionStrings__DefaultConnection=Host=postgres-dataanalysis;Port=5432;Database=grafirio_dataanalysis;Username=dataanalysis_user;Password=…
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

## Kalıcılık

Veritabanı container'larında kalıcı disk **yok**. Azure Files SMB, veritabanı motorlarının
ihtiyaç duyduğu POSIX kilitlemeyi desteklemediği için mount edilemiyor (MongoDB WiredTiger
"Operation not permitted" ile açılmıyor). Bu yüzden şema her yeniden başlatmada sıfırlanır ve
kendini kurabilmelidir:

- Keycloak: `start --import-realm` + [keycloak/realm-grafirio.json](../keycloak/realm-grafirio.json)
- data-analysis-api: açılışta `EnsureCreated()` çağırır — ancak veritabanına ulaşamazsa
  sessizce yutar, bu yüzden bağlantı ayarları düzeldikten sonra servisi yeniden başlatmak gerekir.

Kalıcı veri gerekiyorsa doğru çözüm yönetilen servislere geçmektir (Azure Database for
PostgreSQL, Azure SQL, Cosmos DB for MongoDB).
