namespace Grafirio.Commerce.Api.Modules.Discount;

public class DiscountService(DiscountDbContext db)
{
    public async Task<ServiceResult> CreateAsync(CreateDiscountRequest req, CancellationToken ct)
    {
        var exists = await db.Discounts.AnyAsync(
            x => x.UserId == req.UserId && x.Code == req.Code, ct);

        if (exists)
            return ServiceResult.Error("Discount code already exists for this user", HttpStatusCode.BadRequest);

        var discount = new DiscountEntity
        {
            Id      = NewId.NextSequentialGuid(),
            Code    = req.Code,
            Rate    = req.Rate,
            UserId  = req.UserId,
            Expired = req.Expired,
            Created = DateTime.UtcNow
        };

        await db.Discounts.AddAsync(discount, ct);
        await db.SaveChangesAsync(ct);
        return ServiceResult.SuccessAsNoContent();
    }

    public async Task<ServiceResult<DiscountDto>> GetByCodeAsync(string code, CancellationToken ct)
    {
        var discount = await db.Discounts.FirstOrDefaultAsync(x => x.Code == code, ct);

        if (discount is null)
            return ServiceResult<DiscountDto>.Error("Discount not found", HttpStatusCode.NotFound);

        if (discount.Expired < DateTime.UtcNow)
            return ServiceResult<DiscountDto>.Error("Discount is expired", HttpStatusCode.BadRequest);

        return ServiceResult<DiscountDto>.SuccessAsOk(
            new DiscountDto(discount.Code, discount.Rate, discount.Expired));
    }
}
