using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace Lensee.Modules.Catalog.Data;

public partial class CatalogDbContext : DbContext
{
    public CatalogDbContext(DbContextOptions<CatalogDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Brand> Brands { get; set; }

    public virtual DbSet<Category> Categories { get; set; }

    public virtual DbSet<Product> Products { get; set; }

    public virtual DbSet<Sku> Skus { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("uuid-ossp");

        modelBuilder.Entity<Brand>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("brands_pkey");

            entity.ToTable("brands", "catalog");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Name)
                .HasMaxLength(255)
                .HasColumnName("name");
        });

        modelBuilder.Entity<Category>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("categories_pkey");

            entity.ToTable("categories", "catalog");

            entity.HasIndex(e => e.ParentId, "idx_categories_parent");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.Name)
                .HasMaxLength(255)
                .HasColumnName("name");
            entity.Property(e => e.ParentId).HasColumnName("parent_id");

            entity.HasOne(d => d.Parent).WithMany(p => p.InverseParent)
                .HasForeignKey(d => d.ParentId)
                .HasConstraintName("categories_parent_id_fkey");
        });

        modelBuilder.Entity<Product>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("products_pkey");

            entity.ToTable("products", "catalog", table =>
            {
                table.HasCheckConstraint("chk_products_product_type", "product_type in ('Lens','Solution')");
                table.HasCheckConstraint("chk_products_sell_mode", "sell_mode in ('SealedPackOnly','SinglePiece','Both')");
                table.HasCheckConstraint("chk_products_pieces_per_pack", "pieces_per_pack is null or pieces_per_pack > 0");
                table.HasCheckConstraint("chk_products_sealed_expiry_rate", "sealed_expiry_rate is null or sealed_expiry_rate in ('Daily','Monthly','Annual')");
            });

            entity.HasIndex(e => e.IsActive, "idx_products_active");

            entity.HasIndex(e => e.BrandId, "idx_products_brand");

            entity.HasIndex(e => e.CategoryId, "idx_products_category");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.BrandId).HasColumnName("brand_id");
            entity.Property(e => e.CategoryId).HasColumnName("category_id");
            entity.Property(e => e.ClinicalParams)
                .HasColumnType("jsonb")
                .HasColumnName("clinical_params");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.DeletedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("deleted_at");
            entity.Property(e => e.ExpiryType)
                .HasMaxLength(50)
                .HasColumnName("expiry_type");
            entity.Property(e => e.SealedExpiryDuration)
                .HasMaxLength(50)
                .HasColumnName("sealed_expiry_duration");
            entity.Property(e => e.SealedExpiryRate)
                .HasMaxLength(20)
                .HasColumnName("sealed_expiry_rate");
            entity.Property(e => e.OpenedExpiryDuration)
                .HasMaxLength(50)
                .HasColumnName("opened_expiry_duration");
            entity.Property(e => e.ExtendedAttributes)
                .HasColumnType("jsonb")
                .HasColumnName("extended_attributes");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.Name)
                .HasMaxLength(255)
                .HasColumnName("name");
            entity.Property(e => e.PiecesPerPack).HasColumnName("pieces_per_pack");
            entity.Property(e => e.ProductType)
                .HasMaxLength(50)
                .HasColumnName("product_type");
            entity.Property(e => e.SellMode)
                .HasMaxLength(50)
                .HasColumnName("sell_mode");

            entity.HasOne(d => d.Brand).WithMany(p => p.Products)
                .HasForeignKey(d => d.BrandId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("products_brand_id_fkey");

            entity.HasOne(d => d.Category).WithMany(p => p.Products)
                .HasForeignKey(d => d.CategoryId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("products_category_id_fkey");
        });

        modelBuilder.Entity<Sku>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("skus_pkey");

            entity.ToTable("skus", "catalog", table =>
            {
                table.HasCheckConstraint("chk_skus_power_sign", "power_sign is null or power_sign in ('+','-')");
            });

            entity.HasIndex(e => e.ProductId, "idx_skus_product");

            entity.HasIndex(e => e.SkuCode, "idx_skus_sku_code");

            entity.HasIndex(e => e.SkuCode, "skus_sku_code_key").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.Barcode)
                .HasMaxLength(255)
                .HasColumnName("barcode");
            entity.Property(e => e.ColorName)
                .HasMaxLength(100)
                .HasColumnName("color_name");
            entity.Property(e => e.DeletedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("deleted_at");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.PowerSign)
                .HasMaxLength(1)
                .HasColumnName("power_sign");
            entity.Property(e => e.PowerValue).HasColumnName("power_value");
            entity.Property(e => e.ProductId).HasColumnName("product_id");
            entity.Property(e => e.Size)
                .HasMaxLength(50)
                .HasColumnName("size");
            entity.Property(e => e.SkuCode)
                .HasMaxLength(100)
                .HasColumnName("sku_code");

            entity.HasOne(d => d.Product).WithMany(p => p.Skus)
                .HasForeignKey(d => d.ProductId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("skus_product_id_fkey");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
