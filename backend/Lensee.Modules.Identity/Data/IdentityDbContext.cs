using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace Lensee.Modules.Identity.Data;

public partial class IdentityDbContext : DbContext
{
    public IdentityDbContext(DbContextOptions<IdentityDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<AuditLog> AuditLogs { get; set; }

    public virtual DbSet<RefreshToken> RefreshTokens { get; set; }

    public virtual DbSet<RolesPermission> RolesPermissions { get; set; }

    public virtual DbSet<User> Users { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("uuid-ossp");

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("audit_logs_pkey");

            entity.ToTable("audit_logs", "identity");

            entity.HasIndex(e => e.CreatedAt, "idx_audit_logs_created_at").IsDescending();

            entity.HasIndex(e => new { e.EntityType, e.EntityId }, "idx_audit_logs_entity");

            entity.HasIndex(e => e.UserId, "idx_audit_logs_user");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.Action)
                .HasMaxLength(50)
                .HasColumnName("action");
            entity.Property(e => e.ChangedFields)
                .HasColumnType("jsonb")
                .HasColumnName("changed_fields");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.EntityId).HasColumnName("entity_id");
            entity.Property(e => e.EntityType)
                .HasMaxLength(100)
                .HasColumnName("entity_type");
            entity.Property(e => e.IpAddress)
                .HasMaxLength(45)
                .HasColumnName("ip_address");
            entity.Property(e => e.StockDeltaApplied).HasColumnName("stock_delta_applied");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.User).WithMany(p => p.AuditLogs)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.ClientSetNull)
                .HasConstraintName("audit_logs_user_id_fkey");
        });

        modelBuilder.Entity<RefreshToken>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("refresh_tokens_pkey");

            entity.ToTable("refresh_tokens", "identity");

            entity.HasIndex(e => e.TokenHash, "idx_refresh_tokens_hash");

            entity.HasIndex(e => e.UserId, "idx_refresh_tokens_user");

            entity.HasIndex(e => e.TokenHash, "refresh_tokens_token_hash_key").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.CreatedByIp)
                .HasMaxLength(45)
                .HasColumnName("created_by_ip");
            entity.Property(e => e.ExpiresAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("expires_at");
            entity.Property(e => e.ReplacedBy).HasColumnName("replaced_by");
            entity.Property(e => e.RevokedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("revoked_at");
            entity.Property(e => e.RevokedByIp)
                .HasMaxLength(45)
                .HasColumnName("revoked_by_ip");
            entity.Property(e => e.TokenHash)
                .HasMaxLength(512)
                .HasColumnName("token_hash");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.ReplacedByNavigation).WithMany(p => p.InverseReplacedByNavigation)
                .HasForeignKey(d => d.ReplacedBy)
                .HasConstraintName("refresh_tokens_replaced_by_fkey");

            entity.HasOne(d => d.User).WithMany(p => p.RefreshTokens)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("refresh_tokens_user_id_fkey");
        });

        modelBuilder.Entity<RolesPermission>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("roles_permissions_pkey");

            entity.ToTable("roles_permissions", "identity");

            entity.HasIndex(e => new { e.Role, e.Permission }, "uq_role_permission").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.Permission)
                .HasMaxLength(100)
                .HasColumnName("permission");
            entity.Property(e => e.Role)
                .HasMaxLength(50)
                .HasColumnName("role");
        });

        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("users_pkey");

            entity.ToTable("users", "identity");

            entity.HasIndex(e => e.LocationId, "idx_users_location");

            entity.HasIndex(e => e.Role, "idx_users_role");

            entity.HasIndex(e => e.Username, "users_username_key").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.FullName)
                .HasMaxLength(255)
                .HasColumnName("full_name");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.LocationId).HasColumnName("location_id");
            entity.Property(e => e.PasswordHash)
                .HasMaxLength(255)
                .HasColumnName("password_hash");
            entity.Property(e => e.Role)
                .HasMaxLength(50)
                .HasColumnName("role");
            entity.Property(e => e.Username)
                .HasMaxLength(100)
                .HasColumnName("username");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
