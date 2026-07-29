namespace Grafirio.Identity.Api.Features.Subscriptions.MyAccess;

public record GetMyAccessQuery : IRequestByServiceResult<MyAccessResponse>;

/// <summary>
/// Çağıran kullanıcının şu anda ürünü kullanmaya hakkı var mı.
/// UserAdmin girişte bunu sorar; diğer servisler de aynı ucu kullanarak
/// erişimi zorlayabilir, böylece kural tek yerde tanımlı kalır.
/// </summary>
public record MyAccessResponse(
    bool HasAccess,
    Guid? CompanyId,
    string? Plan,
    DateTime? EndsAt,
    string Reason
);
