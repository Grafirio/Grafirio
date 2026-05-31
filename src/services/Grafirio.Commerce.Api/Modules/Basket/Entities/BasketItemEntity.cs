namespace Grafirio.Commerce.Api.Modules.Basket;

public class BasketItemEntity
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;
    public string? ImageUrl { get; set; }
    public decimal Price { get; set; }
    public decimal? DiscountedPrice { get; set; }

    public BasketItemEntity() { }
    public BasketItemEntity(Guid id, string name, string? imageUrl, decimal price)
    {
        Id = id; Name = name; ImageUrl = imageUrl; Price = price;
    }
}
