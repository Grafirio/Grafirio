namespace Grafirio.Commerce.Api.Modules.Orders;

public class Order
{
    public Guid Id { get; set; }
    public string Code { get; set; } = default!;
    public DateTime Created { get; set; }
    public Guid BuyerId { get; set; }
    public OrderStatus Status { get; set; }
    public decimal TotalPrice { get; set; }
    public float? DiscountRate { get; set; }
    public Guid? PaymentId { get; set; }

    public Address Address { get; set; } = default!;
    public List<OrderItem> Items { get; set; } = [];

    public static Order Create(Guid buyerId, float? discountRate)
    {
        var rnd  = new Random();
        var code = string.Concat(Enumerable.Range(0, 10).Select(_ => rnd.Next(0, 10)));
        return new Order
        {
            Id           = NewId.NextSequentialGuid(),
            Code         = code,
            BuyerId      = buyerId,
            Created      = DateTime.UtcNow,
            Status       = OrderStatus.WaitingForPayment,
            DiscountRate = discountRate
        };
    }

    public void AddItem(Guid productId, string productName, decimal unitPrice)
    {
        Items.Add(new OrderItem { ProductId = productId, ProductName = productName, UnitPrice = unitPrice, OrderId = Id });
        TotalPrice += unitPrice;
    }

    public void SetPaid(Guid paymentId) { Status = OrderStatus.Paid; PaymentId = paymentId; }
}
