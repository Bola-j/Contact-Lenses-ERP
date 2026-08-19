using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace Lensee.Modules.Notifications.Data;

public partial class NotificationsDbContext : DbContext
{
    public NotificationsDbContext(DbContextOptions<NotificationsDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<AlertConfig> AlertConfigs { get; set; }

    public virtual DbSet<NotificationLog> NotificationLogs { get; set; }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        AssignNotificationNumbers();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        AssignNotificationNumbers();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void AssignNotificationNumbers()
    {
        foreach (var entry in ChangeTracker.Entries<NotificationLog>().Where(entry => entry.State == EntityState.Added))
        {
            if (entry.Entity.Id == Guid.Empty) entry.Entity.Id = Guid.NewGuid();
            entry.Entity.NotificationNumber ??= $"NOT-{entry.Entity.Id:N}".ToUpperInvariant();
            if (entry.Entity.ReferenceId is not { } referenceId || !string.IsNullOrWhiteSpace(entry.Entity.ReferenceCode)) continue;

            var prefix = entry.Entity.ReferenceType?.ToLowerInvariant() switch
            {
                "stockbalance" => "BAL",
                "inventorybatch" => "BATCH",
                "operation" => "OP",
                "paymentlog" => "PAY",
                "stocktake" => "STK",
                "supplyshipment" => "SUP",
                "merchant" => "MER",
                "exportlog" => "EXP",
                _ => "REC"
            };
            entry.Entity.ReferenceCode = $"{prefix}-{referenceId:N}".ToUpperInvariant();
        }
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("uuid-ossp");

        modelBuilder.Entity<AlertConfig>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("alert_configs_pkey");

            entity.ToTable("alert_configs", "notifications");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.AlertType)
                .HasMaxLength(100)
                .HasColumnName("alert_type");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.ThresholdUnit)
                .HasMaxLength(50)
                .HasColumnName("threshold_unit");
            entity.Property(e => e.ThresholdValue).HasColumnName("threshold_value");
        });

        modelBuilder.Entity<NotificationLog>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("notification_logs_pkey");

            entity.ToTable("notification_logs", "notifications");

            entity.HasIndex(e => e.CreatedAt, "idx_notif_logs_created_at").IsDescending();

            entity.HasIndex(e => new { e.TargetUserId, e.IsRead }, "idx_notif_logs_user_unread").HasFilter("(is_read = false)");

            entity.HasIndex(e => e.NotificationNumber, "uq_notif_logs_notification_number").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.AlertType)
                .HasMaxLength(100)
                .HasColumnName("alert_type");
            entity.Property(e => e.Channel)
                .HasMaxLength(50)
                .HasDefaultValueSql("'InApp'::character varying")
                .HasColumnName("channel");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.IsRead)
                .HasDefaultValue(false)
                .HasColumnName("is_read");
            entity.Property(e => e.Message).HasColumnName("message");
            entity.Property(e => e.NotificationNumber)
                .IsRequired()
                .HasMaxLength(40)
                .HasColumnName("notification_number");
            entity.Property(e => e.ReferenceId).HasColumnName("reference_id");
            entity.Property(e => e.ReferenceCode)
                .HasMaxLength(40)
                .HasColumnName("reference_code");
            entity.Property(e => e.ReferenceContextJson)
                .HasColumnType("jsonb")
                .HasColumnName("reference_context_json");
            entity.Property(e => e.ReferenceTitle)
                .HasMaxLength(300)
                .HasColumnName("reference_title");
            entity.Property(e => e.ReferenceType)
                .HasMaxLength(100)
                .HasColumnName("reference_type");
            entity.Property(e => e.TargetRole)
                .HasMaxLength(50)
                .HasColumnName("target_role");
            entity.Property(e => e.TargetUserId).HasColumnName("target_user_id");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
