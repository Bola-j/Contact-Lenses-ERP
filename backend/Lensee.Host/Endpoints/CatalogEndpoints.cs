using Lensee.Modules.Catalog.Data;
using Lensee.Modules.Catalog.Domain.Events;
using Lensee.Modules.Catalog.Services;
using Lensee.SharedKernel.Abstractions;
using Lensee.SharedKernel.Primitives;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.EntityFrameworkCore;

namespace Lensee.Host.Endpoints;

public static class CatalogEndpoints
{
    public static RouteGroupBuilder MapCatalogEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/api/v1/catalog")
            .WithTags("Catalog")
            .RequireAuthorization();

        group.MapGet("/categories", ListCategoriesAsync).RequireAuthorization("catalog.read");
        group.MapGet("/categories/tree", ListCategoryTreeAsync).RequireAuthorization("catalog.read");
        group.MapPost("/categories", CreateCategoryAsync).RequireAuthorization("catalog.write");
        group.MapPut("/categories/{id:guid}", UpdateCategoryAsync).RequireAuthorization("catalog.write");

        group.MapGet("/brands", ListBrandsAsync).RequireAuthorization("catalog.read");
        group.MapPost("/brands", CreateBrandAsync).RequireAuthorization("catalog.write");
        group.MapPut("/brands/{id:guid}", UpdateBrandAsync).RequireAuthorization("catalog.write");

        group.MapGet("/products", ListProductsAsync).RequireAuthorization("catalog.read");
        group.MapGet("/products/{id:guid}", GetProductAsync).RequireAuthorization("catalog.read");
        group.MapPost("/products", CreateProductAsync).RequireAuthorization("catalog.write");
        group.MapPut("/products/{id:guid}", UpdateProductAsync).RequireAuthorization("catalog.write");
        group.MapPatch("/products/{id:guid}/deactivate", DeactivateProductAsync).RequireAuthorization("catalog.write");
        group.MapPatch("/products/{id:guid}/reactivate", ReactivateProductAsync).RequireAuthorization("catalog.write");

        group.MapPost("/products/{productId:guid}/skus", CreateSkuAsync).RequireAuthorization("catalog.write");
        group.MapPut("/skus/{id:guid}", UpdateSkuAsync).RequireAuthorization("catalog.write");
        group.MapPatch("/skus/{id:guid}/deactivate", DeactivateSkuAsync).RequireAuthorization("catalog.write");
        group.MapPatch("/skus/{id:guid}/reactivate", ReactivateSkuAsync).RequireAuthorization("catalog.write");

        return group;
    }

    private static async Task<Ok<IReadOnlyList<CategoryTreeResponse>>> ListCategoryTreeAsync(
        CatalogDbContext dbContext,
        CategoryTreeService categoryTreeService,
        CancellationToken cancellationToken)
    {
        var categories = await dbContext.Categories
            .OrderBy(category => category.Name)
            .Select(category => new CategoryTreeItem(category.Id, category.ParentId, category.Name))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok<IReadOnlyList<CategoryTreeResponse>>(categoryTreeService.BuildTree(categories).Select(ToResponse).ToList());
    }

    private static async Task<Ok<IReadOnlyList<CategoryResponse>>> ListCategoriesAsync(
        CatalogDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var categories = await dbContext.Categories
            .OrderBy(category => category.Name)
            .Select(category => new CategoryResponse(category.Id, category.ParentId, category.Name, category.CreatedAt))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok<IReadOnlyList<CategoryResponse>>(categories);
    }

    private static async Task<Results<Created<CategoryResponse>, ValidationProblem, NotFound>> CreateCategoryAsync(
        CategoryRequest request,
        CatalogDbContext dbContext,
        IClock clock,
        IAuditLogWriter auditLogWriter,
        ICatalogEventPublisher eventPublisher,
        CancellationToken cancellationToken)
    {
        var errors = ValidateName(request.Name, nameof(request.Name), "Category name is required.");
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        if (request.ParentId is not null && await dbContext.Categories.FindAsync([request.ParentId], cancellationToken) is null)
        {
            return TypedResults.NotFound();
        }

        var category = new Category
        {
            Id = Guid.NewGuid(),
            ParentId = request.ParentId,
            Name = request.Name.Trim(),
            CreatedAt = clock.EgyptNow
        };

        dbContext.Categories.Add(category);
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditLogWriter.WriteAsync("Category", category.Id, "Create", new { category.Name, category.ParentId }, cancellationToken: cancellationToken);
        await eventPublisher.PublishAsync(new CategoryCreated(category.Id, clock.EgyptNow), cancellationToken);

        return TypedResults.Created($"/api/v1/catalog/categories/{category.Id}", ToResponse(category));
    }

    private static async Task<Results<Ok<CategoryResponse>, ValidationProblem, NotFound>> UpdateCategoryAsync(
        Guid id,
        CategoryRequest request,
        CatalogDbContext dbContext,
        CategoryTreeService categoryTreeService,
        IClock clock,
        IAuditLogWriter auditLogWriter,
        ICatalogEventPublisher eventPublisher,
        CancellationToken cancellationToken)
    {
        var errors = ValidateName(request.Name, nameof(request.Name), "Category name is required.");
        if (request.ParentId == id)
        {
            errors[nameof(request.ParentId)] = ["A category cannot be its own parent."];
        }

        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var category = await dbContext.Categories.FindAsync([id], cancellationToken);
        if (category is null)
        {
            return TypedResults.NotFound();
        }

        if (request.ParentId is not null && await dbContext.Categories.FindAsync([request.ParentId], cancellationToken) is null)
        {
            return TypedResults.NotFound();
        }

        if (request.ParentId is not null && await categoryTreeService.WouldCreateCycleAsync(dbContext, id, request.ParentId.Value, cancellationToken))
        {
            errors[nameof(request.ParentId)] = ["A category cannot be moved under one of its descendants."];
            return TypedResults.ValidationProblem(errors);
        }

        category.Name = request.Name.Trim();
        category.ParentId = request.ParentId;
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditLogWriter.WriteAsync("Category", category.Id, "Update", new { category.Name, category.ParentId }, cancellationToken: cancellationToken);
        await eventPublisher.PublishAsync(new CategoryUpdated(category.Id, clock.EgyptNow), cancellationToken);

        return TypedResults.Ok(ToResponse(category));
    }

    private static async Task<Ok<IReadOnlyList<BrandResponse>>> ListBrandsAsync(
        CatalogDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var brands = await dbContext.Brands
            .OrderBy(brand => brand.Name)
            .Select(brand => new BrandResponse(brand.Id, brand.Name, brand.CreatedAt))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok<IReadOnlyList<BrandResponse>>(brands);
    }

    private static async Task<Results<Created<BrandResponse>, ValidationProblem>> CreateBrandAsync(
        BrandRequest request,
        CatalogDbContext dbContext,
        IClock clock,
        IAuditLogWriter auditLogWriter,
        ICatalogEventPublisher eventPublisher,
        CancellationToken cancellationToken)
    {
        var errors = ValidateName(request.Name, nameof(request.Name), "Brand name is required.");
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var brand = new Brand { Id = Guid.NewGuid(), Name = request.Name.Trim(), CreatedAt = clock.EgyptNow };
        dbContext.Brands.Add(brand);
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditLogWriter.WriteAsync("Brand", brand.Id, "Create", new { brand.Name }, cancellationToken: cancellationToken);
        await eventPublisher.PublishAsync(new BrandCreated(brand.Id, clock.EgyptNow), cancellationToken);

        return TypedResults.Created($"/api/v1/catalog/brands/{brand.Id}", ToResponse(brand));
    }

    private static async Task<Results<Ok<BrandResponse>, ValidationProblem, NotFound>> UpdateBrandAsync(
        Guid id,
        BrandRequest request,
        CatalogDbContext dbContext,
        IClock clock,
        IAuditLogWriter auditLogWriter,
        ICatalogEventPublisher eventPublisher,
        CancellationToken cancellationToken)
    {
        var errors = ValidateName(request.Name, nameof(request.Name), "Brand name is required.");
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var brand = await dbContext.Brands.FindAsync([id], cancellationToken);
        if (brand is null)
        {
            return TypedResults.NotFound();
        }

        brand.Name = request.Name.Trim();
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditLogWriter.WriteAsync("Brand", brand.Id, "Update", new { brand.Name }, cancellationToken: cancellationToken);
        await eventPublisher.PublishAsync(new BrandUpdated(brand.Id, clock.EgyptNow), cancellationToken);

        return TypedResults.Ok(ToResponse(brand));
    }

    private static async Task<Ok<PagedResult<ProductListResponse>>> ListProductsAsync(
        string? search,
        bool? includeInactive,
        int? page,
        int? pageSize,
        CatalogDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var request = new PageRequest(page ?? 1, pageSize ?? 25);
        var query = dbContext.Products
            .Include(product => product.Brand)
            .Include(product => product.Category)
            .AsQueryable();

        if (includeInactive == false)
        {
            query = query.Where(product => product.IsActive && product.DeletedAt == null);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            var pattern = $"%{search.Trim()}%";
            query = query.Where(product =>
                EF.Functions.ILike(product.Name, pattern) ||
                EF.Functions.ILike(product.Brand.Name, pattern) ||
                EF.Functions.ILike(product.Category.Name, pattern));
        }

        var total = await query.CountAsync(cancellationToken);
        var products = await query
            .OrderBy(product => product.Name)
            .Skip(request.Skip)
            .Take(request.PageSize)
            .Select(product => new ProductListResponse(
                product.Id,
                product.Name,
                product.ProductType,
                product.Brand.Name,
                product.Category.Name,
                product.PiecesPerPack,
                product.SellMode,
                product.SealedExpiryDuration,
                product.SealedExpiryRate,
                product.OpenedExpiryDuration,
                product.IsActive))
            .ToListAsync(cancellationToken);

        return TypedResults.Ok(new PagedResult<ProductListResponse>(products, request.Page, request.PageSize, total));
    }

    private static async Task<Results<Ok<ProductDetailResponse>, NotFound>> GetProductAsync(
        Guid id,
        CatalogDbContext dbContext,
        CancellationToken cancellationToken)
    {
        var product = await dbContext.Products
            .Include(value => value.Brand)
            .Include(value => value.Category)
            .Include(value => value.Skus.OrderBy(sku => sku.SkuCode))
            .SingleOrDefaultAsync(value => value.Id == id, cancellationToken);

        return product is null ? TypedResults.NotFound() : TypedResults.Ok(ToDetailResponse(product));
    }

    private static async Task<Results<Created<ProductDetailResponse>, ValidationProblem, NotFound>> CreateProductAsync(
        ProductRequest request,
        CatalogDbContext dbContext,
        IClock clock,
        IAuditLogWriter auditLogWriter,
        ICatalogEventPublisher eventPublisher,
        CancellationToken cancellationToken)
    {
        var errors = CatalogValidation.ValidateProduct(ToValidationInput(request));
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        if (!await ReferencesExistAsync(dbContext, request.CategoryId, request.BrandId, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        var product = new Product
        {
            Id = Guid.NewGuid(),
            CategoryId = request.CategoryId,
            BrandId = request.BrandId,
            Name = request.Name.Trim(),
            ProductType = CatalogValidation.NormalizeProductType(request.ProductType),
            ExpiryType = CatalogValidation.NormalizeExpiryType(request.ExpiryType),
            SealedExpiryDuration = null,
            SealedExpiryRate = null,
            OpenedExpiryDuration = CatalogValidation.NormalizeOpenedDuration(request.OpenedExpiryDuration),
            PiecesPerPack = request.PiecesPerPack,
            SellMode = CatalogValidation.NormalizeSellMode(request.SellMode),
            ClinicalParams = NormalizeJson(request.ClinicalParams),
            ExtendedAttributes = NormalizeJson(request.ExtendedAttributes),
            IsActive = true,
            CreatedAt = clock.EgyptNow
        };

        dbContext.Products.Add(product);
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditLogWriter.WriteAsync("Product", product.Id, "Create", new { product.Name, product.ProductType }, cancellationToken: cancellationToken);
        await eventPublisher.PublishAsync(new ProductCreated(product.Id, clock.EgyptNow), cancellationToken);

        return TypedResults.Created($"/api/v1/catalog/products/{product.Id}", ToDetailResponse(await LoadProductAsync(dbContext, product.Id, cancellationToken)));
    }

    private static async Task<Results<Ok<ProductDetailResponse>, ValidationProblem, NotFound>> UpdateProductAsync(
        Guid id,
        ProductRequest request,
        CatalogDbContext dbContext,
        IClock clock,
        IAuditLogWriter auditLogWriter,
        ICatalogEventPublisher eventPublisher,
        CancellationToken cancellationToken)
    {
        var errors = CatalogValidation.ValidateProduct(ToValidationInput(request));
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var product = await dbContext.Products.FindAsync([id], cancellationToken);
        if (product is null || !await ReferencesExistAsync(dbContext, request.CategoryId, request.BrandId, cancellationToken))
        {
            return TypedResults.NotFound();
        }

        product.CategoryId = request.CategoryId;
        product.BrandId = request.BrandId;
        product.Name = request.Name.Trim();
        product.ProductType = CatalogValidation.NormalizeProductType(request.ProductType);
        product.ExpiryType = CatalogValidation.NormalizeExpiryType(request.ExpiryType);
        product.SealedExpiryDuration = null;
        product.SealedExpiryRate = null;
        product.OpenedExpiryDuration = CatalogValidation.NormalizeOpenedDuration(request.OpenedExpiryDuration);
        product.PiecesPerPack = request.PiecesPerPack;
        product.SellMode = CatalogValidation.NormalizeSellMode(request.SellMode);
        product.ClinicalParams = NormalizeJson(request.ClinicalParams);
        product.ExtendedAttributes = NormalizeJson(request.ExtendedAttributes);

        await dbContext.SaveChangesAsync(cancellationToken);
        await auditLogWriter.WriteAsync("Product", product.Id, "Update", new { product.Name, product.ProductType }, cancellationToken: cancellationToken);
        await eventPublisher.PublishAsync(new ProductUpdated(product.Id, clock.EgyptNow), cancellationToken);

        return TypedResults.Ok(ToDetailResponse(await LoadProductAsync(dbContext, product.Id, cancellationToken)));
    }

    private static Task<Results<Ok<ProductDetailResponse>, NotFound>> DeactivateProductAsync(
        Guid id,
        CatalogDbContext dbContext,
        IClock clock,
        IAuditLogWriter auditLogWriter,
        ICatalogEventPublisher eventPublisher,
        CancellationToken cancellationToken) =>
        SetProductActiveStateAsync(id, false, dbContext, clock, auditLogWriter, eventPublisher, cancellationToken);

    private static Task<Results<Ok<ProductDetailResponse>, NotFound>> ReactivateProductAsync(
        Guid id,
        CatalogDbContext dbContext,
        IClock clock,
        IAuditLogWriter auditLogWriter,
        ICatalogEventPublisher eventPublisher,
        CancellationToken cancellationToken) =>
        SetProductActiveStateAsync(id, true, dbContext, clock, auditLogWriter, eventPublisher, cancellationToken);

    private static async Task<Results<Created<SkuResponse>, ValidationProblem, Conflict, NotFound>> CreateSkuAsync(
        Guid productId,
        SkuRequest request,
        CatalogDbContext dbContext,
        SkuCodeGenerator skuCodeGenerator,
        IClock clock,
        IAuditLogWriter auditLogWriter,
        ICatalogEventPublisher eventPublisher,
        CancellationToken cancellationToken)
    {
        var product = await dbContext.Products
            .Include(value => value.Brand)
            .Include(value => value.Category)
            .SingleOrDefaultAsync(value => value.Id == productId, cancellationToken);
        if (product is null)
        {
            return TypedResults.NotFound();
        }

        var errors = CatalogValidation.ValidateSku(ToValidationInput(request), product.ProductType);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var skuCode = skuCodeGenerator.Generate(product, ToSkuCodeInput(request));
        if (await dbContext.Skus.AnyAsync(sku => sku.SkuCode == skuCode, cancellationToken))
        {
            return TypedResults.Conflict();
        }

        var sku = new Sku
        {
            Id = Guid.NewGuid(),
            ProductId = productId,
            SkuCode = skuCode,
            PowerSign = request.PowerSign,
            PowerValue = request.PowerValue,
            ColorName = request.ColorName?.Trim(),
            Size = request.Size?.Trim(),
            Barcode = request.Barcode?.Trim(),
            IsActive = true
        };

        dbContext.Skus.Add(sku);
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditLogWriter.WriteAsync("Sku", sku.Id, "Create", new { sku.SkuCode, sku.ProductId }, cancellationToken: cancellationToken);
        await eventPublisher.PublishAsync(new SkuCreated(sku.Id, clock.EgyptNow), cancellationToken);

        return TypedResults.Created($"/api/v1/catalog/skus/{sku.Id}", ToResponse(sku));
    }

    private static async Task<Results<Ok<SkuResponse>, ValidationProblem, Conflict, NotFound>> UpdateSkuAsync(
        Guid id,
        SkuRequest request,
        CatalogDbContext dbContext,
        SkuCodeGenerator skuCodeGenerator,
        IClock clock,
        IAuditLogWriter auditLogWriter,
        ICatalogEventPublisher eventPublisher,
        CancellationToken cancellationToken)
    {
        var sku = await dbContext.Skus
            .Include(value => value.Product)
            .ThenInclude(value => value.Brand)
            .Include(value => value.Product)
            .ThenInclude(value => value.Category)
            .SingleOrDefaultAsync(value => value.Id == id, cancellationToken);
        if (sku is null)
        {
            return TypedResults.NotFound();
        }

        var errors = CatalogValidation.ValidateSku(ToValidationInput(request), sku.Product.ProductType);
        if (errors.Count > 0)
        {
            return TypedResults.ValidationProblem(errors);
        }

        var skuCode = skuCodeGenerator.Generate(sku.Product, ToSkuCodeInput(request));
        if (await dbContext.Skus.AnyAsync(value => value.Id != id && value.SkuCode == skuCode, cancellationToken))
        {
            return TypedResults.Conflict();
        }

        sku.SkuCode = skuCode;
        sku.PowerSign = request.PowerSign;
        sku.PowerValue = request.PowerValue;
        sku.ColorName = request.ColorName?.Trim();
        sku.Size = request.Size?.Trim();
        sku.Barcode = request.Barcode?.Trim();

        await dbContext.SaveChangesAsync(cancellationToken);
        await auditLogWriter.WriteAsync("Sku", sku.Id, "Update", new { sku.SkuCode }, cancellationToken: cancellationToken);
        await eventPublisher.PublishAsync(new SkuUpdated(sku.Id, clock.EgyptNow), cancellationToken);

        return TypedResults.Ok(ToResponse(sku));
    }

    private static Task<Results<Ok<SkuResponse>, NotFound>> DeactivateSkuAsync(
        Guid id,
        CatalogDbContext dbContext,
        IClock clock,
        IAuditLogWriter auditLogWriter,
        ICatalogEventPublisher eventPublisher,
        CancellationToken cancellationToken) =>
        SetSkuActiveStateAsync(id, false, dbContext, clock, auditLogWriter, eventPublisher, cancellationToken);

    private static Task<Results<Ok<SkuResponse>, NotFound>> ReactivateSkuAsync(
        Guid id,
        CatalogDbContext dbContext,
        IClock clock,
        IAuditLogWriter auditLogWriter,
        ICatalogEventPublisher eventPublisher,
        CancellationToken cancellationToken) =>
        SetSkuActiveStateAsync(id, true, dbContext, clock, auditLogWriter, eventPublisher, cancellationToken);

    private static async Task<Results<Ok<ProductDetailResponse>, NotFound>> SetProductActiveStateAsync(
        Guid id,
        bool isActive,
        CatalogDbContext dbContext,
        IClock clock,
        IAuditLogWriter auditLogWriter,
        ICatalogEventPublisher eventPublisher,
        CancellationToken cancellationToken)
    {
        var product = await dbContext.Products.FindAsync([id], cancellationToken);
        if (product is null)
        {
            return TypedResults.NotFound();
        }

        product.IsActive = isActive;
        product.DeletedAt = isActive ? null : clock.EgyptNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditLogWriter.WriteAsync("Product", product.Id, isActive ? "Reactivate" : "Deactivate", new { product.IsActive }, cancellationToken: cancellationToken);
        await eventPublisher.PublishAsync(
            isActive ? new ProductReactivated(product.Id, clock.EgyptNow) : new ProductDeactivated(product.Id, clock.EgyptNow),
            cancellationToken);

        return TypedResults.Ok(ToDetailResponse(await LoadProductAsync(dbContext, product.Id, cancellationToken)));
    }

    private static async Task<Results<Ok<SkuResponse>, NotFound>> SetSkuActiveStateAsync(
        Guid id,
        bool isActive,
        CatalogDbContext dbContext,
        IClock clock,
        IAuditLogWriter auditLogWriter,
        ICatalogEventPublisher eventPublisher,
        CancellationToken cancellationToken)
    {
        var sku = await dbContext.Skus.FindAsync([id], cancellationToken);
        if (sku is null)
        {
            return TypedResults.NotFound();
        }

        sku.IsActive = isActive;
        sku.DeletedAt = isActive ? null : clock.EgyptNow;
        await dbContext.SaveChangesAsync(cancellationToken);
        await auditLogWriter.WriteAsync("Sku", sku.Id, isActive ? "Reactivate" : "Deactivate", new { sku.IsActive }, cancellationToken: cancellationToken);
        await eventPublisher.PublishAsync(
            isActive ? new SkuReactivated(sku.Id, clock.EgyptNow) : new SkuDeactivated(sku.Id, clock.EgyptNow),
            cancellationToken);

        return TypedResults.Ok(ToResponse(sku));
    }

    private static async Task<bool> ReferencesExistAsync(
        CatalogDbContext dbContext,
        Guid categoryId,
        Guid brandId,
        CancellationToken cancellationToken) =>
        await dbContext.Categories.AnyAsync(category => category.Id == categoryId, cancellationToken) &&
        await dbContext.Brands.AnyAsync(brand => brand.Id == brandId, cancellationToken);

    private static async Task<Product> LoadProductAsync(CatalogDbContext dbContext, Guid id, CancellationToken cancellationToken) =>
        await dbContext.Products
            .Include(value => value.Brand)
            .Include(value => value.Category)
            .Include(value => value.Skus.OrderBy(sku => sku.SkuCode))
            .SingleAsync(value => value.Id == id, cancellationToken);

    private static ProductValidationInput ToValidationInput(ProductRequest request) =>
        new(request.Name, request.ProductType, request.ExpiryType, request.SealedExpiryDuration, request.SealedExpiryRate, request.OpenedExpiryDuration, request.PiecesPerPack, request.SellMode, request.ClinicalParams, request.ExtendedAttributes);

    private static SkuValidationInput ToValidationInput(SkuRequest request) =>
        new(request.PowerSign, request.PowerValue, request.ColorName, request.Size);

    private static SkuCodeInput ToSkuCodeInput(SkuRequest request) =>
        new(request.PowerSign, request.PowerValue, request.ColorName, request.Size);

    private static string? NormalizeJson(string? json) =>
        string.IsNullOrWhiteSpace(json) ? null : json.Trim();

    private static string? NormalizeText(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static Dictionary<string, string[]> ValidateName(string? name, string key, string message)
    {
        var errors = new Dictionary<string, string[]>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(name))
        {
            errors[key] = [message];
        }

        return errors;
    }

    private static CategoryResponse ToResponse(Category category) =>
        new(category.Id, category.ParentId, category.Name, category.CreatedAt);

    private static CategoryTreeResponse ToResponse(CategoryTreeNode node) =>
        new(node.Id, node.ParentId, node.Name, node.Children.Select(ToResponse).ToList());

    private static BrandResponse ToResponse(Brand brand) =>
        new(brand.Id, brand.Name, brand.CreatedAt);

    private static SkuResponse ToResponse(Sku sku) =>
        new(sku.Id, sku.ProductId, sku.SkuCode, sku.PowerSign, sku.PowerValue, sku.ColorName, sku.Size, sku.Barcode, sku.IsActive, sku.DeletedAt);

    private static ProductDetailResponse ToDetailResponse(Product product) =>
        new(
            product.Id,
            product.CategoryId,
            product.Category.Name,
            product.BrandId,
            product.Brand.Name,
            product.Name,
            product.ProductType,
            product.ExpiryType,
            product.SealedExpiryDuration,
            product.SealedExpiryRate,
            product.OpenedExpiryDuration,
            product.PiecesPerPack,
            product.SellMode,
            product.ClinicalParams,
            product.ExtendedAttributes,
            product.IsActive,
            product.DeletedAt,
            product.CreatedAt,
            product.Skus.Select(ToResponse).ToList());
}

public sealed record CategoryRequest(Guid? ParentId, string Name);

public sealed record CategoryResponse(Guid Id, Guid? ParentId, string Name, DateTime CreatedAt);

public sealed record CategoryTreeResponse(
    Guid Id,
    Guid? ParentId,
    string Name,
    IReadOnlyList<CategoryTreeResponse> Children);

public sealed record BrandRequest(string Name);

public sealed record BrandResponse(Guid Id, string Name, DateTime CreatedAt);

public sealed record ProductRequest(
    Guid CategoryId,
    Guid BrandId,
    string Name,
    string ProductType,
    string? ExpiryType,
    string? SealedExpiryDuration,
    string? SealedExpiryRate,
    string? OpenedExpiryDuration,
    int? PiecesPerPack,
    string? SellMode,
    string? ClinicalParams,
    string? ExtendedAttributes);

public sealed record ProductListResponse(
    Guid Id,
    string Name,
    string ProductType,
    string BrandName,
    string CategoryName,
    int? PiecesPerPack,
    string? SellMode,
    string? SealedExpiryDuration,
    string? SealedExpiryRate,
    string? OpenedExpiryDuration,
    bool IsActive);

public sealed record ProductDetailResponse(
    Guid Id,
    Guid CategoryId,
    string CategoryName,
    Guid BrandId,
    string BrandName,
    string Name,
    string ProductType,
    string? ExpiryType,
    string? SealedExpiryDuration,
    string? SealedExpiryRate,
    string? OpenedExpiryDuration,
    int? PiecesPerPack,
    string? SellMode,
    string? ClinicalParams,
    string? ExtendedAttributes,
    bool IsActive,
    DateTime? DeletedAt,
    DateTime CreatedAt,
    IReadOnlyList<SkuResponse> Skus);

public sealed record SkuRequest(
    string? PowerSign,
    decimal? PowerValue,
    string? ColorName,
    string? Size,
    string? Barcode);

public sealed record SkuResponse(
    Guid Id,
    Guid ProductId,
    string SkuCode,
    string? PowerSign,
    decimal? PowerValue,
    string? ColorName,
    string? Size,
    string? Barcode,
    bool IsActive,
    DateTime? DeletedAt);
