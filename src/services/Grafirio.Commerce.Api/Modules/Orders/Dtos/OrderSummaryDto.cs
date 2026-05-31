namespace Grafirio.Commerce.Api.Modules.Orders;

public record OrderSummaryDto(
    Guid Id,
    string Code,
    DateTime Created,
    decimal TotalPrice,
    OrderStatus Status,
    List<OrderItemDto> Items);
