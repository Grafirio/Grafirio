---
applyTo: "src/services/**"
description: "Yeni microservice oluştururken ve mevcut .NET microservice'lerde uygulanan Clean Architecture kuralları"
---

# Microservice Kuralları (Clean Architecture)

Bu kurallar `src/services/**` altındaki tüm .NET microservice'lere uygulanır. `.github/copilot-instructions.md` ve `.github/instructions/dotnet.instructions.md` içindeki kurallar da geçerlidir — özellikle **ONAYLANDI kuralı**.

---

## 1. Mimari: Clean Architecture (Zorunlu)

Yeni her microservice **4 katmanlı** Clean Architecture ile kurulur:

```
src/services/Grafirio.{Domain}.Api/
├── Domain/                  # En iç katman — kimseye bağlı değil
│   ├── Entities/
│   ├── ValueObjects/
│   ├── Events/
│   ├── Exceptions/
│   └── Interfaces/          # Repository sözleşmeleri (IOrderRepository)
│
├── Application/             # Use case'ler, iş kuralları
│   ├── UseCases/ (veya Handlers/)
│   ├── Dtos/
│   ├── Validators/
│   ├── Mappers/
│   └── Interfaces/          # IEmailService, IPaymentGateway
│
├── Infrastructure/          # Dış dünya — DB, mesaj kuyruğu, dış API
│   ├── Persistence/
│   │   ├── DbContext.cs
│   │   ├── Configurations/
│   │   └── Migrations/
│   ├── Repositories/
│   ├── Services/            # Email, Payment implementasyonları
│   └── Messaging/           # RabbitMQ, MassTransit
│
└── Api/                     # Sunum katmanı — HTTP, DI, middleware
    ├── Controllers/ (veya Endpoints/)
    ├── Middleware/
    ├── Extensions/          # ServiceCollection extension'ları
    └── Program.cs
```

### Bağımlılık Yönü

```
Api ──────► Application ──────► Domain
                                  ▲
Infrastructure ───────────────────┘
```

- **Domain** hiçbir katmana bağlı değildir. NuGet bağımlılığı bile minimum (sadece .NET base).
- **Application** sadece `Domain`'e bağlıdır.
- **Infrastructure** `Application` ve `Domain`'e bağlıdır.
- **Api** her şeye bağlanabilir ama iş mantığı içermez.

---

## 2. Katman Sorumlulukları

### Domain
- Entity, Value Object, Domain Event.
- Repository **interface**'leri (implementasyon Infrastructure'da).
- Domain Exception sınıfları.
- **Bağımlılık yok**: EF Core, ASP.NET, vb. referans verilmez.

### Application
- Use case / handler sınıfları (örn. `CreateOrderHandler`).
- DTO'lar (Request / Response).
- `FluentValidation` validator'ları.
- `IEmailService`, `IPaymentGateway` gibi dış servis **interface**'leri.
- CQRS kullanılıyorsa Command / Query / Handler buraya.

### Infrastructure
- EF Core `DbContext` ve entity configuration.
- Repository implementasyonları.
- Email / SMS / Payment gibi dış servis implementasyonları.
- RabbitMQ publisher / consumer.
- Migration dosyaları.

### Api
- Controller veya minimal API endpoint'leri.
- `Program.cs` — DI, middleware pipeline.
- Authentication / Authorization konfigürasyonu.
- Swagger setup.
- **İş mantığı yok** — sadece handler/service çağırır.

---

## 3. Zorunlu Bileşenler

Her microservice şunları içermek zorunda:

| Bileşen | Detay |
|---|---|
| Health check endpoint | `/health` (Liveness), `/health/ready` (Readiness) |
| Swagger | Development ortamında açık |
| Serilog | Structured logging, console + dosya/seq sink |
| Authentication | Keycloak / JWT Bearer (Identity hariç) |
| Gateway routing | `Grafirio.Gateway/appsettings.json` içine YARP route eklenir |
| Dockerfile | Multi-stage build (sdk → runtime) |
| docker-compose entry | `docker-compose.yml` içine eklenir |
| CORS | Frontend origin'leri allow edilir |
| Exception middleware | Global exception handler |

---

## 4. Port Tahsisi

- [README.md](README.md) içindeki port tablosuna yeni servis port'u eklenir.
- Çakışan port verilmez. Mevcut tahsisli portlar:
  - `5000` Gateway
  - `5036` Identity
  - `5280` Catalog
  - `5023` Basket
  - `5221` Data Analysis
- Yeni servis için sırayla devam et: `5100`, `5200` gibi mantıklı bir aralık seç.

---

## 5. Logging — Serilog Setup

`Program.cs` içinde:

```csharp
builder.Host.UseSerilog((context, configuration) =>
{
    configuration
        .ReadFrom.Configuration(context.Configuration)
        .Enrich.FromLogContext()
        .Enrich.WithProperty("ServiceName", "Grafirio.Orders.Api");
});
```

`appsettings.json` içinde:

```json
{
  "Serilog": {
    "MinimumLevel": {
      "Default": "Information",
      "Override": {
        "Microsoft": "Warning",
        "System": "Warning"
      }
    },
    "WriteTo": [
      { "Name": "Console" }
    ]
  }
}
```

---

## 6. Migration Politikası

- Migration dosyaları **manuel** olarak `dotnet ef migrations add <Name>` ile eklenir.
- Migration adı açıklayıcı olur: `AddOrderTable`, `AddIndexOnCustomerEmail`.
- Migration uygulanmadan önce kullanıcıya plan sunulur (ONAYLANDI kuralı).
- Production migration'ı CI/CD pipeline veya `dotnet ef database update` ile elle yapılır; otomatik `Database.Migrate()` çağrısı `Program.cs` içinde olmaz (veya feature flag arkasında olur).

---

## 7. Authentication

- Identity dışındaki tüm API'ler Keycloak JWT doğrulaması yapar.
- `Authority` ve `Audience` `appsettings.json` üzerinden alınır.
- `[Authorize]` attribute'u controller veya endpoint seviyesinde kullanılır.
- Public endpoint'ler `[AllowAnonymous]` ile işaretlenir.

---

## 8. Inter-Service Communication

- Senkron iletişim için **HTTP** ve **Gateway** üzerinden çağrı yapılır.
- Asenkron iletişim için **RabbitMQ + MassTransit** kullanılır.
- Servisler birbirlerinin DB'sine doğrudan erişmez. Her servis kendi DB'sinin sahibidir.

---

## 9. Test (Opsiyonel ama Önerilen)

- Unit test projesi: `Grafirio.{Domain}.Api.Tests` adıyla aynı klasör seviyesinde.
- Framework: xUnit.
- `Application` ve `Domain` katmanı yüksek coverage'da test edilir.
- `Infrastructure` integration test ile doğrulanır.

---

## 10. Yeni Microservice Oluştururken Checklist

Bir microservice oluşturma talebi geldiğinde plan aşamasında şunları teyit et:

- [ ] Servis adı ve domain'i belli mi?
- [ ] Port numarası seçildi mi?
- [ ] DB teknolojisi (SQL Server / PostgreSQL / MongoDB)?
- [ ] Authentication gerekli mi?
- [ ] RabbitMQ entegrasyonu var mı?
- [ ] Gateway route eklenecek mi?
- [ ] Frontend bu servise erişecek mi (CORS)?
- [ ] Docker'a eklenecek mi?

Hepsi onaylandıktan sonra `ONAYLANDI` beklenir.
