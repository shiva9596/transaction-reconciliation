using Microsoft.EntityFrameworkCore;
using TransactionReconciliation.Console.Domain.Entities;

namespace TransactionReconciliation.Console.Data;

public class AppDbContext : DbContext
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
    }

    public DbSet<TransactionRecord> Transactions => Set<TransactionRecord>();
    public DbSet<TransactionAudit> TransactionAudits => Set<TransactionAudit>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<TransactionRecord>(entity =>
        {
            entity.HasKey(x => x.TransactionId);

            entity.Property(x => x.TransactionId)
                .HasMaxLength(100);

            entity.Property(x => x.CardHash)
                .HasMaxLength(256);

            entity.Property(x => x.CardLast4)
                .HasMaxLength(4);

            entity.Property(x => x.LocationCode)
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(x => x.ProductName)
                .HasMaxLength(200)
                .IsRequired();

            entity.Property(x => x.Amount)
                .HasPrecision(18, 2);

            entity.HasIndex(x => x.TransactionTimeUtc);
            entity.HasIndex(x => x.Status);
        });

        modelBuilder.Entity<TransactionAudit>(entity =>
        {
            entity.HasKey(x => x.Id);

            entity.Property(x => x.TransactionId)
                .HasMaxLength(100)
                .IsRequired();

            entity.Property(x => x.FieldName)
                .HasMaxLength(100);

            entity.Property(x => x.OldValue)
                .HasMaxLength(500);

            entity.Property(x => x.NewValue)
                .HasMaxLength(500);

            entity.Property(x => x.RunId)
                .HasMaxLength(100)
                .IsRequired();

            entity.HasIndex(x => x.TransactionId);
            entity.HasIndex(x => x.RunId);
            entity.HasIndex(x => x.ChangedAtUtc);
        });
    }
}