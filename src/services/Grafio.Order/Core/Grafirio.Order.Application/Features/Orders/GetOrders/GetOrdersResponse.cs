using Grafirio.Order.Application.Features.Orders.CreateOrder;

namespace Grafirio.Order.Application.Features.Orders.GetOrders;

public record GetOrdersResponse(DateTime Created, decimal TotalPrice, List<OrderItemDto> Items);