using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace Lensee.Modules.CRM.Data;

public partial class CrmDbContext : DbContext
{
    public CrmDbContext(DbContextOptions<CrmDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<Merchant> Merchants { get; set; }

    public virtual DbSet<MerchantNote> MerchantNotes { get; set; }

    public virtual DbSet<Representative> Representatives { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("uuid-ossp");

        modelBuilder.Entity<Merchant>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("merchants_pkey");

            entity.ToTable("merchants", "crm", table =>
            {
                table.HasCheckConstraint("chk_merchants_status", "status in ('Active','Inactive')");
            });

            entity.HasIndex(e => e.Status, "idx_merchants_status").HasFilter("(is_deleted = false)");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.Address).HasColumnName("address");
            entity.Property(e => e.BusinessName)
                .HasMaxLength(255)
                .HasColumnName("business_name");
            entity.Property(e => e.BusinessType)
                .HasMaxLength(50)
                .HasColumnName("business_type");
            entity.Property(e => e.ContactPersonName)
                .HasMaxLength(255)
                .HasColumnName("contact_person_name");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.DeletedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("deleted_at");
            entity.Property(e => e.Email)
                .HasMaxLength(255)
                .HasColumnName("email");
            entity.Property(e => e.IsDeleted)
                .HasDefaultValue(false)
                .HasColumnName("is_deleted");
            entity.Property(e => e.Notes).HasColumnName("notes");
            entity.Property(e => e.PhoneNumbers)
                .HasDefaultValueSql("'{}'::text[]")
                .HasColumnName("phone_numbers");
            entity.Property(e => e.Status)
                .HasMaxLength(50)
                .HasDefaultValueSql("'Active'::character varying")
                .HasColumnName("status");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_at");
        });

        modelBuilder.Entity<MerchantNote>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("merchant_notes_pkey");

            entity.ToTable("merchant_notes", "crm");

            entity.HasIndex(e => e.MerchantId, "idx_merchant_notes_merchant");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.AddedBy).HasColumnName("added_by");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.MerchantId).HasColumnName("merchant_id");
            entity.Property(e => e.Note).HasColumnName("note");

            entity.HasOne(d => d.Merchant).WithMany(p => p.MerchantNotes)
                .HasForeignKey(d => d.MerchantId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("merchant_notes_merchant_id_fkey");
        });

        modelBuilder.Entity<Representative>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("representatives_pkey");

            entity.ToTable("representatives", "crm", table =>
            {
                table.HasCheckConstraint("chk_representatives_status", "status in ('Active','Inactive')");
                table.HasCheckConstraint("chk_representatives_type", "type in ('Internal','External')");
            });

            entity.HasIndex(e => e.Status, "idx_representatives_status").HasFilter("(is_deleted = false)");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.AssignedLocationId).HasColumnName("assigned_location_id");
            entity.Property(e => e.DeletedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("deleted_at");
            entity.Property(e => e.Email)
                .HasMaxLength(255)
                .HasColumnName("email");
            entity.Property(e => e.IsDeleted)
                .HasDefaultValue(false)
                .HasColumnName("is_deleted");
            entity.Property(e => e.LinkedUserId).HasColumnName("linked_user_id");
            entity.Property(e => e.Name)
                .HasMaxLength(255)
                .HasColumnName("name");
            entity.Property(e => e.Notes).HasColumnName("notes");
            entity.Property(e => e.PhoneNumbers)
                .HasDefaultValueSql("'{}'::text[]")
                .HasColumnName("phone_numbers");
            entity.Property(e => e.Status)
                .HasMaxLength(50)
                .HasDefaultValueSql("'Active'::character varying")
                .HasColumnName("status");
            entity.Property(e => e.Type)
                .HasMaxLength(50)
                .HasDefaultValueSql("'External'::character varying")
                .HasColumnName("type");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
