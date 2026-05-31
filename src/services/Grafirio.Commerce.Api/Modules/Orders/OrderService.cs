namespace Grafirio.Commerce.Api.Modules.Orders;

public class OrderService(OrderDbContext db, IIdentityService identity)
{
    public async Task<ServiceResult> CreateAsync(CreateOrderRequest req, CancellationToken ct)
    {
        if (!req.Items.Any())
            return ServiceResult.Error("Order must have at least one item", HttpStatusCode.BadRequest);

        var address = new Address
        {
            Province = req.Address.Province,
            District = req.Address.District,
            Street   = req.Address.Street,
            ZipCode  = req.Address.ZipCode,
            Line     = req.Address.Line
        };


        var order = Order.Create(identity.UserId, req.DiscountRate);
        order.Address = address;

        foreach (var item in req.Items)
            order.AddItem(item.ProductId, item.ProductName, item.UnitPrice);

        db.Orders.Add(order);
        await db.SaveChangesAsync(ct);

        // Payment gerçekleştiğinde order.SetPaid(paymentId) çağrılır
        return ServiceResult.SuccessAsNoContent();
    }

    public async Task<ServiceResult<List<OrderSummaryDto>>> GetMyOrdersAsync(CancellationToken ct)
    {
        var orders = await db.Orders
            .Include(x => x.Items)
            .Where(x => x.BuyerId == identity.UserId)
            .OrderByDescending(x => x.Created)
            .ToListAsync(ct);

        var result = orders.Select(o => new OrderSummaryDto(
            o.Id,
            o.Code,
            o.Created,
            o.TotalPrice,
            o.Status,
            o.Items.Select(i => new OrderItemDto(i.ProductId, i.ProductName, i.UnitPrice)).ToList()
        )).ToList();

        return ServiceResult<List<OrderSummaryDto>>.SuccessAsOk(result);
    }
}
