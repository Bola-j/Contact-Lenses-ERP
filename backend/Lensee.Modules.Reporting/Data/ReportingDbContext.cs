using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore;

namespace Lensee.Modules.Reporting.Data;

public partial class ReportingDbContext : DbContext
{
    public ReportingDbContext(DbContextOptions<ReportingDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<ExportLog> ExportLogs { get; set; }
    public virtual DbSet<DocumentExportJob> DocumentExportJobs { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("uuid-ossp");

        modelBuilder.Entity<ExportLog>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("export_logs_pkey");

            entity.ToTable("export_logs", "reporting");

            entity.Property(e => e.Id)
                .HasDefaultValueSql("uuid_generate_v4()")
                .HasColumnName("id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP")
                .HasColumnType("timestamp without time zone")
                .HasColumnName("created_at");
            entity.Property(e => e.GeneratedUrl)
                .HasMaxLength(500)
                .HasColumnName("generated_url");
            entity.Property(e => e.ReportType)
                .HasMaxLength(50)
                .HasColumnName("report_type");
            entity.Property(e => e.RequestedBy).HasColumnName("requested_by");
        });

        modelBuilder.Entity<DocumentExportJob>(entity =>
        {
            entity.HasKey(e => e.Id).HasName("document_export_jobs_pkey");
            entity.ToTable("document_export_jobs", "reporting");
            entity.Property(e => e.Id).HasDefaultValueSql("uuid_generate_v4()").HasColumnName("id");
            entity.Property(e => e.DocumentKey).HasMaxLength(100).HasColumnName("document_key");
            entity.Property(e => e.Format).HasMaxLength(16).HasColumnName("format");
            entity.Property(e => e.Language).HasMaxLength(16).HasColumnName("language");
            entity.Property(e => e.Status).HasMaxLength(20).HasColumnName("status");
            entity.Property(e => e.RequestedBy).HasColumnName("requested_by");
            entity.Property(e => e.RequestedByRole).HasMaxLength(50).HasColumnName("requested_by_role");
            entity.Property(e => e.RowCount).HasColumnName("row_count");
            entity.Property(e => e.SnapshotJson).HasColumnType("jsonb").HasColumnName("snapshot_json");
            entity.Property(e => e.Content).HasColumnName("content");
            entity.Property(e => e.FileName).HasMaxLength(300).HasColumnName("file_name");
            entity.Property(e => e.ContentType).HasMaxLength(150).HasColumnName("content_type");
            entity.Property(e => e.Error).HasMaxLength(2000).HasColumnName("error");
            entity.Property(e => e.IdempotencyKey).HasMaxLength(100).HasColumnName("idempotency_key");
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP").HasColumnType("timestamp without time zone").HasColumnName("created_at");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP").HasColumnType("timestamp without time zone").HasColumnName("updated_at");
            entity.Property(e => e.ExpiresAt).HasColumnType("timestamp without time zone").HasColumnName("expires_at");
            entity.HasIndex(e => new { e.RequestedBy, e.IdempotencyKey }).IsUnique().HasFilter("idempotency_key is not null").HasDatabaseName("ux_document_export_jobs_requester_idempotency");
            entity.HasIndex(e => new { e.Status, e.CreatedAt }).HasDatabaseName("ix_document_export_jobs_queue");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
