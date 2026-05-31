using Asp.Versioning.Builder;

namespace Grafirio.Commerce.Api.Modules.Catalog;

public static class CatalogEndpoints
{
    public static void Map(WebApplication app, ApiVersionSet vs)
    {
        // Products — path "catalogs" korunuyor (mevcut gateway route'u değişmesin)
        var products = app
            .MapGroup("api/v{version:apiVersion}/catalogs")
            .WithTags("Products")
            .WithApiVersionSet(vs)
            .RequireAuthorization();

        products.MapGet("/",
            async (CatalogService svc, CancellationToken ct) =>
                (await svc.GetAllProductsAsync(ct)).ToGenericResult())
            .WithName("GetAllProducts").MapToApiVersion(1, 0);

        products.MapGet("/{id:guid}",
            async (Guid id, CatalogService svc, CancellationToken ct) =>
                (await svc.GetProductByIdAsync(id, ct)).ToGenericResult())
            .WithName("GetProductById").MapToApiVersion(1, 0);

        products.MapGet("/my",
            async (CatalogService svc, CancellationToken ct) =>
                (await svc.GetProductsByUserAsync(ct)).ToGenericResult())
            .WithName("GetMyProducts").MapToApiVersion(1, 0);

        products.MapPost("/",
            async (CreateProductRequest req,
                   IValidator<CreateProductRequest> validator,
                   CatalogService svc,
                   CancellationToken ct) =>
            {
                var v = await validator.ValidateAsync(req, ct);
                if (!v.IsValid) return Results.ValidationProblem(v.ToDictionary());
                return (await svc.CreateProductAsync(req, ct)).ToGenericResult();
            })
            .WithName("CreateProduct").MapToApiVersion(1, 0);

        products.MapPut("/{id:guid}",
            async (Guid id,
                   UpdateProductRequest req,
                   IValidator<UpdateProductRequest> validator,
                   CatalogService svc,
                   CancellationToken ct) =>
            {
                var v = await validator.ValidateAsync(req, ct);
                if (!v.IsValid) return Results.ValidationProblem(v.ToDictionary());
                return (await svc.UpdateProductAsync(id, req, ct)).ToGenericResult();
            })
            .WithName("UpdateProduct").MapToApiVersion(1, 0);

        products.MapDelete("/{id:guid}",
            async (Guid id, CatalogService svc, CancellationToken ct) =>
                (await svc.DeleteProductAsync(id, ct)).ToGenericResult())
            .WithName("DeleteProduct").MapToApiVersion(1, 0);

        // Categories
        var categories = app
            .MapGroup("api/v{version:apiVersion}/categories")
            .WithTags("Categories")
            .WithApiVersionSet(vs)
            .RequireAuthorization();

        categories.MapGet("/",
            async (CatalogService svc, CancellationToken ct) =>
                (await svc.GetAllCategoriesAsync(ct)).ToGenericResult())
            .WithName("GetAllCategories").MapToApiVersion(1, 0);

        categories.MapGet("/{id:guid}",
            async (Guid id, CatalogService svc, CancellationToken ct) =>
                (await svc.GetCategoryByIdAsync(id, ct)).ToGenericResult())
            .WithName("GetCategoryById").MapToApiVersion(1, 0);

        categories.MapPost("/",
            async (CreateCategoryRequest req,
                   IValidator<CreateCategoryRequest> validator,
                   CatalogService svc,
                   CancellationToken ct) =>
            {
                var v = await validator.ValidateAsync(req, ct);
                if (!v.IsValid) return Results.ValidationProblem(v.ToDictionary());
                return (await svc.CreateCategoryAsync(req, ct)).ToGenericResult();
            })
            .WithName("CreateCategory").MapToApiVersion(1, 0);
    }
}
