namespace Grafirio.Discount.Api.Features.Discounts.CreateDiscount
{
    public record CreateDiscountCommand(string Code, float Rate, Guid UserId, DateTime Expired) : IRequestByServiceResult;
}