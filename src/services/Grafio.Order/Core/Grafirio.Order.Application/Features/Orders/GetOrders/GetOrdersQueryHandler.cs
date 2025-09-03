using AutoMapper;
using MediatR;
using Grafirio.Order.Application.Contracts.Repositories;
using Grafirio.Order.Application.Features.Orders.CreateOrder;
using Grafirio.Shared;
using Grafirio.Shared.Services;

namespace Grafirio.Order.Application.Features.Orders.GetOrders;

public class GetOrdersQueryHandler(IIdentityService identityService, IOrderRepository orderRepository, IMapper mapper) : IRequestHandler<GetOrdersQuery, ServiceResult<List<GetOrdersResponse>>>
{
    public async Task<ServiceResult<List<GetOrdersResponse>>> Handle(GetOrdersQuery request, CancellationToken cancellationToken)
    {
        var orders = await orderRepository.GetOrderByBuyerId(identityService.UserId);


        var response = orders.Select(o => new GetOrdersResponse(o.Created, o.TotalPrice, mapper.Map<List<OrderItemDto>>(o.OrderItems))).ToList();


        return ServiceResult<List<GetOrdersResponse>>.SuccessAsOk(response);
    }
}