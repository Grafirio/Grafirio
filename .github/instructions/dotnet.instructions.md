---
applyTo: "**/*.cs"
description: ".NET / C# kod kuralları — dosya bölme, Clean Code, Serilog, secret yönetimi"
---

# .NET / C# Kod Kuralları

Bu kurallar tüm `.cs` dosyalarına uygulanır. `.github/copilot-instructions.md` içindeki genel kurallar geçerliliğini korur — özellikle **ONAYLANDI kuralı**.

---

## 1. Dosya Yapısı ve Bölme

- **1 dosya = 1 sınıf** (gerekirse `partial class`).
- **Sayısal satır limiti yok**, ama şu durumlarda mutlaka böl:
  - Sınıf birden fazla iş yapıyorsa (SRP ihlali).
  - Dosyayı baştan sona okumak için defalarca kaydırmak gerekiyorsa.
  - Bir bölge (region) bağımsız bir kavram ifade ediyorsa.
- DTO, Entity, Service, Controller, Validator, Mapper, Repository, Configuration **kendi klasörlerinde**, kendi dosyalarında bulunur.
- İç içe sınıf (nested class) sadece gerçek anlamda kapsayan sınıfa ait ise kullanılır; aksi halde ayrı dosyaya çıkarılır.

---

## 2. Klasör Yapısı

Feature / modül bazlı klasörleme tercih edilir:

```
Modules/Orders/
├── Controllers/
│   └── OrdersController.cs
├── Services/
│   ├── IOrderService.cs
│   └── OrderService.cs
├── Repositories/
│   ├── IOrderRepository.cs
│   └── OrderRepository.cs
├── Dtos/
│   ├── CreateOrderRequest.cs
│   └── OrderResponse.cs
├── Validators/
│   └── CreateOrderRequestValidator.cs
├── Mappers/
│   └── OrderMapper.cs
└── Entities/
    └── Order.cs
```

---

## 3. Sınıf ve Metot Kuralları

- **Tek sorumluluk**: Bir sınıf bir iş yapar.
- **Metot uzun ise alt private metotlara böl.** Bir metot ekranı aşıyorsa parçala.
- **Constructor injection** kullanılır; service locator yasak.
- Private field: `_camelCase`, `readonly`.
- Public property: `PascalCase`.

```csharp
public sealed class OrderService : IOrderService
{
    private readonly IOrderRepository _orderRepository;
    private readonly ILogger<OrderService> _logger;

    public OrderService(IOrderRepository orderRepository, ILogger<OrderService> logger)
    {
        _orderRepository = orderRepository;
        _logger = logger;
    }
}
```

---

## 4. Async / Await

- Tüm I/O işlemleri `async`.
- Metot adı `Async` suffix'i ile biter: `GetByIdAsync`, `CreateAsync`.
- `CancellationToken` parametre olarak alınır ve aşağı geçilir.
- `async void` yasak (event handler hariç).
- `.Result`, `.Wait()`, `.GetAwaiter().GetResult()` yasak.

---

## 5. Naming

| Öğe | Kural | Örnek |
|---|---|---|
| Sınıf, struct, enum, metot, property | `PascalCase` | `OrderService` |
| Interface | `I` + `PascalCase` | `IOrderService` |
| Private field | `_camelCase` | `_orderRepository` |
| Local variable, parameter | `camelCase` | `orderId` |
| Constant | `PascalCase` | `MaxRetryCount` |
| Async metot | suffix `Async` | `CreateAsync` |

---

## 6. Logging — Serilog (Zorunlu)

- Logger her zaman `ILogger<T>` üzerinden injection ile alınır. Serilog backend olarak `Program.cs` içinde konfigüre edilir.
- **Structured logging zorunlu**. String concatenation / interpolation log içinde yasak.

**Doğru:**
```csharp
_logger.LogInformation("Order {OrderId} created for customer {CustomerId}", order.Id, order.CustomerId);
```

**Yanlış:**
```csharp
_logger.LogInformation($"Order {order.Id} created for customer {order.CustomerId}");
_logger.LogInformation("Order " + order.Id + " created");
```

- Exception log'ta exception parametre olarak verilir:

```csharp
_logger.LogError(ex, "Failed to create order for customer {CustomerId}", customerId);
```

- Log seviyeleri:
  - `LogTrace` / `LogDebug` → geliştirme detayı
  - `LogInformation` → iş akışı (sipariş oluştu, kullanıcı login)
  - `LogWarning` → beklenen ama dikkat çeken durum
  - `LogError` → hata yakalandı
  - `LogCritical` → sistem çalışamaz hale geldi

---

## 7. Exception Handling

- Hata susturulmaz. `catch { }` yasak.
- Yakalanan exception ya işlenir, ya loglanır, ya yukarı fırlatılır.
- `throw ex;` yasak → stack trace bozulur. `throw;` kullan.
- Domain hataları için özel exception sınıfı tanımla (`OrderNotFoundException` gibi).

---

## 8. Hassas Veri (.NET'e Özel)

- `appsettings.json` ve `appsettings.Development.json` içinde gerçek parola, key, secret **bulunmaz**.
- Geliştirme: `dotnet user-secrets set "ConnectionStrings:Default" "..."`.
- Production / Docker: environment variable (`ConnectionStrings__Default`).
- Connection string `appsettings.json` içinde sadece host/port/database bilgisi içerir; kullanıcı adı ve parola environment'tan gelir.

---

## 9. Dependency Injection

- Servisler interface üzerinden register edilir.
- Lifetime doğru seçilir:
  - `AddSingleton` → durumsuz, thread-safe
  - `AddScoped` → request başına (DbContext, Service)
  - `AddTransient` → her çağrıda yeni (Validator, Mapper)
- Registration kodu `Program.cs` içinde extension method olarak gruplanır:

```csharp
public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddOrderModule(this IServiceCollection services)
    {
        services.AddScoped<IOrderService, OrderService>();
        services.AddScoped<IOrderRepository, OrderRepository>();
        return services;
    }
}
```

---

## 10. EF Core

- Entity konfigürasyonu `IEntityTypeConfiguration<T>` ile ayrı dosyada.
- Migration dosyaları `Infrastructure/Migrations/` altında.
- `DbContext` içinde iş mantığı yok — sadece `DbSet` ve `OnModelCreating`.
- N+1 query'den kaçın; gerektiğinde `Include` veya projection kullan.

---

## 11. API Tasarımı

- Controller ince olur; iş mantığı Service katmanında.
- Action başına `[ProducesResponseType]` attribute'ları.
- Validation `FluentValidation` veya DataAnnotations ile; controller içinde manuel `if` yok.
- DTO → Entity dönüşümü Mapper sınıfında (AutoMapper veya manuel).
