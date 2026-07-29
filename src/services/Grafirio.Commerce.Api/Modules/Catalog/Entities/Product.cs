namespace Grafirio.Commerce.Api.Modules.Catalog;

public class Product
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;
    public string Description { get; set; } = default!;
    public decimal Price { get; set; }
    public Guid UserId { get; set; }
    public string? ImageUrl { get; set; }
    public DateTime Created { get; set; }
    public Guid CategoryId { get; set; }
    public ProductFeature Feature { get; set; } = default!;

    /// <summary>
    /// Bu ürün bir abonelik planını temsil ediyorsa planın adı (TRIAL,
    /// STANDARD, ENTERPRISE); sıradan üründe null.
    ///
    /// Planlar katalogda ürün olarak duruyor ki tek bir sepet ve ödeme akışı
    /// yeterli olsun. Ödeme alındığında bu alan, siparişin bir erişim hakkına
    /// dönüşüp dönüşmeyeceğini belirler.
    /// </summary>
    public string? SubscriptionPlan { get; set; }
}
