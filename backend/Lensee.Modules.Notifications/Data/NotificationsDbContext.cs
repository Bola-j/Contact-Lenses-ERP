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
            entity.Property(e => e.ReferenceId).HasColumnName("reference_id");
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
