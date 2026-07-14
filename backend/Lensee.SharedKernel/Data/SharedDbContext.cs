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

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}
