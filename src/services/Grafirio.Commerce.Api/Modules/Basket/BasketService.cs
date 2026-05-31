using Microsoft.Extensions.Caching.Distributed;

namespace Grafirio.Commerce.Api.Modules.Basket;

public class BasketService(IIdentityService identityService, IDistributedCache cache)
{
    private const string CacheKeyTemplate = "basket:{0}";
    private string Key => string.Format(CacheKeyTemplate, identityService.UserId);

    // ── Internal cache helpers ────────────────────────────────────────────────

    private async Task<BasketEntity?> GetRawAsync(CancellationToken ct)
    {
        var json = await cache.GetStringAsync(Key, ct);
        return string.IsNullOrEmpty(json) ? null : JsonSerializer.Deserialize<BasketEntity>(json);
    }

    private async Task SaveAsync(BasketEntity basket, CancellationToken ct) =>
        await cache.SetStringAsync(Key, JsonSerializer.Serialize(basket), ct);

    // ── Public API ────────────────────────────────────────────────────────────

    public async Task<ServiceResult<BasketDto>> GetBasketAsync(CancellationToken ct)
    {
        var basket = await GetRawAsync(ct);
        if (basket is null)
            return ServiceResult<BasketDto>.Error("Basket not found", HttpStatusCode.NotFound);

        return ServiceResult<BasketDto>.SuccessAsOk(BasketMapper.ToDto(basket));
    }

    public async Task<ServiceResult> AddItemAsync(AddBasketItemRequest req, CancellationToken ct)
    {
        var basket = await GetRawAsync(ct)
            ?? new BasketEntity(identityService.UserId, []);

        var existing = basket.Items.FirstOrDefault(x => x.Id == req.ProductId);
        if (existing is not null) basket.Items.Remove(existing);

        basket.Items.Add(new BasketItemEntity(req.ProductId, req.ProductName, req.ImageUrl, req.Price));
        basket.ApplyExistingDiscount();

        await SaveAsync(basket, ct);
        return ServiceResult.SuccessAsNoContent();
    }

    public async Task<ServiceResult> DeleteItemAsync(Guid itemId, CancellationToken ct)
    {
        var basket = await GetRawAsync(ct);
        if (basket is null)
            return ServiceResult.Error("Basket not found", HttpStatusCode.NotFound);

        var item = basket.Items.FirstOrDefault(x => x.Id == itemId);
        if (item is null)
            return ServiceResult.Error("Item not found", HttpStatusCode.NotFound);

        basket.Items.Remove(item);
        await SaveAsync(basket, ct);
        return ServiceResult.SuccessAsNoContent();
    }

    public async Task<ServiceResult> ApplyDiscountAsync(ApplyDiscountRequest req, CancellationToken ct)
    {
        var basket = await GetRawAsync(ct);
        if (basket is null)
            return ServiceResult.Error("Basket not found", HttpStatusCode.NotFound);

        if (!basket.Items.Any())
            return ServiceResult.Error("Basket is empty", HttpStatusCode.BadRequest);

        basket.ApplyDiscount(req.Coupon, req.Rate);
        await SaveAsync(basket, ct);
        return ServiceResult.SuccessAsNoContent();
    }

    public async Task<ServiceResult> RemoveDiscountAsync(CancellationToken ct)
    {
        var basket = await GetRawAsync(ct);
        if (basket is null)
            return ServiceResult.Error("Basket not found", HttpStatusCode.NotFound);

        basket.ClearDiscount();
        await SaveAsync(basket, ct);
        return ServiceResult.SuccessAsNoContent();
    }
}
