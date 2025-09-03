using MediatR;
using System.Net;
using Grafirio.Order.Application.Contracts.Repositories;
using Grafirio.Order.Application.Contracts.UnitOfWork;
using Grafirio.Order.Domain.Entities;
using Grafirio.Shared;
using Grafirio.Shared.Services;

namespace Grafirio.Order.Application.Features.Orders.CreateOrder;

public class CreateOrderCommandHandler(IOrderRepository orderRepository, IGenericRepository<int, Address> addressRepository, IIdentityService identityService, IUnitOfWork unitOfWork) : IRequestHandler<CreateOrderCommand, ServiceResult>
{
    public async Task<ServiceResult> Handle(CreateOrderCommand request, CancellationToken cancellationToken)
    {
        if (!request.Items.Any()) return ServiceResult.Error("Order items not found", "Order must have at least one item", HttpStatusCode.BadRequest);


        var newAddress = new Address
        {
            Province = request.Address.Province,
            District = request.Address.District,
            Street = request.Address.Street,
            ZipCode = request.Address.ZipCode,
            Line = request.Address.Line
        };


        var order = Domain.Entities.Order.CreateUnPaidOrder(identityService.UserId, request.DiscountRate, newAddress.Id);
        foreach (var orderItem in request.Items) order.AddOrderItem(orderItem.ProductId, orderItem.ProductName, orderItem.UnitPrice);


        order.Address = newAddress;


        orderRepository.Add(order);
        await unitOfWork.CommitAsync(cancellationToken);

        var paymentId = Guid.Empty;


        //Payment işlemleri yapılacak

        order.SetPaidStatus(paymentId);

        orderRepository.Update(order);
        await unitOfWork.CommitAsync(cancellationToken);


        return ServiceResult.SuccessAsNoContent();
    }
}