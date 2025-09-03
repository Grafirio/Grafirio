using AutoMapper;
using Grafirio.Order.Application.Features.Orders.CreateOrder;
using Grafirio.Order.Domain.Entities;

namespace Grafirio.Order.Application.Features.Orders;

public class OrderMapping : Profile
{
    public OrderMapping()
    {
        CreateMap<OrderItem, OrderItemDto>().ReverseMap();
    }
}