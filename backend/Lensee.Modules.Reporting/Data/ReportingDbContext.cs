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

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
