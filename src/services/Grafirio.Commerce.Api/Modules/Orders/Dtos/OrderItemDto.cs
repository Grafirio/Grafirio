namespace Grafirio.Commerce.Api.Modules.Orders;

public record OrderItemDto(Guid ProductId, string ProductName, decimal UnitPrice);
