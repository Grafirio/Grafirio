using Grafirio.Shared;

namespace Grafirio.Order.Application.Features.Orders.GetOrders;

public record GetOrdersQuery : IRequestByServiceResult<List<GetOrdersResponse>>;