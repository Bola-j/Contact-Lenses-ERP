namespace Lensee.Modules.Catalog.Domain.Events;

public abstract record CatalogEvent(Guid EntityId, string EntityType, DateTime OccurredAt);

public sealed record CategoryCreated(Guid EntityId, DateTime OccurredAt)
    : CatalogEvent(EntityId, "Category", OccurredAt);

public sealed record CategoryUpdated(Guid EntityId, DateTime OccurredAt)
    : CatalogEvent(EntityId, "Category", OccurredAt);

public sealed record BrandCreated(Guid EntityId, DateTime OccurredAt)
    : CatalogEvent(EntityId, "Brand", OccurredAt);

public sealed record BrandUpdated(Guid EntityId, DateTime OccurredAt)
    : CatalogEvent(EntityId, "Brand", OccurredAt);

public sealed record ProductCreated(Guid EntityId, DateTime OccurredAt)
    : CatalogEvent(EntityId, "Product", OccurredAt);

public sealed record ProductUpdated(Guid EntityId, DateTime OccurredAt)
    : CatalogEvent(EntityId, "Product", OccurredAt);

public sealed record ProductDeactivated(Guid EntityId, DateTime OccurredAt)
    : CatalogEvent(EntityId, "Product", OccurredAt);

public sealed record ProductReactivated(Guid EntityId, DateTime OccurredAt)
    : CatalogEvent(EntityId, "Product", OccurredAt);

public sealed record SkuCreated(Guid EntityId, DateTime OccurredAt)
    : CatalogEvent(EntityId, "Sku", OccurredAt);

public sealed record SkuUpdated(Guid EntityId, DateTime OccurredAt)
    : CatalogEvent(EntityId, "Sku", OccurredAt);

public sealed record SkuDeactivated(Guid EntityId, DateTime OccurredAt)
    : CatalogEvent(EntityId, "Sku", OccurredAt);

public sealed record SkuReactivated(Guid EntityId, DateTime OccurredAt)
    : CatalogEvent(EntityId, "Sku", OccurredAt);
