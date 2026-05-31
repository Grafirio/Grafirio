namespace Grafirio.Commerce.Api.Modules.Orders;

public class OrderItem
{
    public int Id { get; set; }
    public Guid ProductId { get; set; }
    public string ProductName { get; set; } = default!;
    public decimal UnitPrice { get; set; }
    public Guid OrderId { get; set; }
}
