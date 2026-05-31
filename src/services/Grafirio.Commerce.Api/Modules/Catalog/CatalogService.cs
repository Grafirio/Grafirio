namespace Grafirio.Commerce.Api.Modules.Catalog;

public class CatalogService(CatalogDbContext db, IIdentityService identity)
{
    // ── Products ──────────────────────────────────────────────────────────────

    public async Task<ServiceResult<List<ProductDto>>> GetAllProductsAsync(CancellationToken ct)
    {
        var products = await db.Products.ToListAsync(ct);
        return ServiceResult<List<ProductDto>>.SuccessAsOk(products.Select(CatalogMapper.ToDto).ToList());
    }

    public async Task<ServiceResult<ProductDto>> GetProductByIdAsync(Guid id, CancellationToken ct)
    {
        var product = await db.Products.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (product is null)
            return ServiceResult<ProductDto>.Error("Product not found", HttpStatusCode.NotFound);

        return ServiceResult<ProductDto>.SuccessAsOk(CatalogMapper.ToDto(product));
    }

    public async Task<ServiceResult<List<ProductDto>>> GetProductsByUserAsync(CancellationToken ct)
    {
        var products = await db.Products
            .Where(x => x.UserId == identity.UserId)
            .ToListAsync(ct);
        return ServiceResult<List<ProductDto>>.SuccessAsOk(products.Select(CatalogMapper.ToDto).ToList());
    }

    public async Task<ServiceResult<Guid>> CreateProductAsync(CreateProductRequest req, CancellationToken ct)
    {
        var categoryExists = await db.Categories.AnyAsync(x => x.Id == req.CategoryId, ct);
        if (!categoryExists)
            return ServiceResult<Guid>.Error("Category not found", HttpStatusCode.NotFound);

        var nameExists = await db.Products.AnyAsync(x => x.Name == req.Name, ct);
        if (nameExists)
            return ServiceResult<Guid>.Error("Product name already exists", HttpStatusCode.BadRequest);

        var product = new Product
        {
            Id          = NewId.NextSequentialGuid(),
            Name        = req.Name,
            Description = req.Description,
            Price       = req.Price,
            ImageUrl    = req.ImageUrl,
            CategoryId  = req.CategoryId,
            UserId      = identity.UserId,
            Created     = DateTime.UtcNow,
            Feature     = new ProductFeature { Duration = 0, Rating = 0, EducatorFullName = identity.UserName ?? "" }
        };

        db.Products.Add(product);
        await db.SaveChangesAsync(ct);
        return ServiceResult<Guid>.SuccessAsCreated(product.Id, $"/api/v1/catalogs/{product.Id}");
    }

    public async Task<ServiceResult> UpdateProductAsync(Guid id, UpdateProductRequest req, CancellationToken ct)
    {
        var product = await db.Products.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (product is null)
            return ServiceResult.Error("Product not found", HttpStatusCode.NotFound);

        product.Name        = req.Name;
        product.Description = req.Description;
        product.Price       = req.Price;
        product.ImageUrl    = req.ImageUrl;
        product.CategoryId  = req.CategoryId;

        await db.SaveChangesAsync(ct);
        return ServiceResult.SuccessAsNoContent();
    }

    public async Task<ServiceResult> DeleteProductAsync(Guid id, CancellationToken ct)
    {
        var product = await db.Products.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (product is null)
            return ServiceResult.Error("Product not found", HttpStatusCode.NotFound);

        db.Products.Remove(product);
        await db.SaveChangesAsync(ct);
        return ServiceResult.SuccessAsNoContent();
    }

    // ── Categories ────────────────────────────────────────────────────────────

    public async Task<ServiceResult<List<CategoryDto>>> GetAllCategoriesAsync(CancellationToken ct)
    {
        var cats = await db.Categories.ToListAsync(ct);
        return ServiceResult<List<CategoryDto>>.SuccessAsOk(cats.Select(CatalogMapper.ToDto).ToList());
    }

    public async Task<ServiceResult<CategoryDto>> GetCategoryByIdAsync(Guid id, CancellationToken ct)
    {
        var cat = await db.Categories.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (cat is null)
            return ServiceResult<CategoryDto>.Error("Category not found", HttpStatusCode.NotFound);

        return ServiceResult<CategoryDto>.SuccessAsOk(CatalogMapper.ToDto(cat));
    }

    public async Task<ServiceResult<Guid>> CreateCategoryAsync(CreateCategoryRequest req, CancellationToken ct)
    {
        var cat = new Category { Id = NewId.NextSequentialGuid(), Name = req.Name };
        db.Categories.Add(cat);
        await db.SaveChangesAsync(ct);
        return ServiceResult<Guid>.SuccessAsCreated(cat.Id, $"/api/v1/categories/{cat.Id}");
    }
}
