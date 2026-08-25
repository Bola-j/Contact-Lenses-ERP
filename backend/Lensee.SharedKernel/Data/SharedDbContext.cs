using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace Lensee.SharedKernel.Data;

public partial class SharedDbContext : DbContext
{
    public SharedDbContext(DbContextOptions<SharedDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<SystemSetting> SystemSettings { get; set; }

    public virtual DbSet<OutboxMessage> OutboxMessages { get; set; }

    public virtual DbSet<OutboxDeliveryReceipt> OutboxDeliveryReceipts { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("uuid-ossp");

        modelBuilder.Entity<SystemSetting>(entity =>
        {
            entity.HasKey(e => e.Key).HasName("system_settings_pkey");

            entity.ToTable("system_settings", "shared");

            entity.Property(e => e.Key)
                .HasMaxLength(100)
                .HasColumnName("key");
            entity.Property(e => e.Description).HasColumnName("description");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("updated_at");
            entity.Property(e => e.Value).HasColumnName("value");
        });

        modelBuilder.Entity<OutboxMessage>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("outbox_messages_pkey");

            entity.ToTable("outbox_messages", "shared", table =>
            {
                table.HasCheckConstraint("chk_outbox_status", "status in ('Pending','Processing','Processed','Failed','DeadLetter')");
                table.HasCheckConstraint("chk_outbox_attempts", "attempts >= 0");
            });

            entity.HasIndex(e => new { e.Status, e.NextAttemptAt }, "idx_outbox_messages_ready");
            entity.HasIndex(e => e.OccurredAt, "idx_outbox_messages_occurred_at");
            entity.HasIndex(e => e.CorrelationId, "idx_outbox_messages_correlation");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.Attempts).HasColumnName("attempts");
            entity.Property(e => e.EventType)
                .HasMaxLength(200)
                .HasColumnName("event_type");
            entity.Property(e => e.EventVersion).HasDefaultValue(1).HasColumnName("event_version");
            entity.Property(e => e.CorrelationId).HasMaxLength(128).HasColumnName("correlation_id");
            entity.Property(e => e.CausationId).HasMaxLength(128).HasColumnName("causation_id");
            entity.Property(e => e.LastError).HasColumnName("last_error");
            entity.Property(e => e.NextAttemptAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("next_attempt_at");
            entity.Property(e => e.OccurredAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("occurred_at");
            entity.Property(e => e.Payload)
                .HasColumnType("jsonb")
                .HasColumnName("payload");
            entity.Property(e => e.ProcessedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("processed_at");
            entity.Property(e => e.Status)
                .HasMaxLength(50)
                .HasColumnName("status");
        });

        modelBuilder.Entity<OutboxDeliveryReceipt>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("outbox_delivery_receipts_pkey");

            entity.ToTable("outbox_delivery_receipts", "shared");

            entity.HasIndex(e => new { e.OutboxMessageId, e.HandlerName }, "uq_outbox_delivery_receipts_message_handler").IsUnique();

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.HandlerName)
                .HasMaxLength(300)
                .HasColumnName("handler_name");
            entity.Property(e => e.OutboxMessageId).HasColumnName("outbox_message_id");
            entity.Property(e => e.ProcessedAt)
                .HasColumnType("timestamp without time zone")
                .HasColumnName("processed_at");

            entity.HasOne(d => d.OutboxMessage).WithMany()
                .HasForeignKey(d => d.OutboxMessageId)
                .OnDelete(DeleteBehavior.Cascade)
                .HasConstraintName("outbox_delivery_receipts_outbox_message_id_fkey");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
