using Microsoft.EntityFrameworkCore;
using TransCard.Models.Entities;

namespace TransCard.Data;

public class PaymentsDbContext(DbContextOptions<PaymentsDbContext> options) : DbContext(options)
{
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentEvent> PaymentEvents => Set<PaymentEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Payment>(entity =>
        {
            entity.HasKey(p => p.Id);

            entity.Property(p => p.Id)
                  .ValueGeneratedOnAdd();

            // Unique constraint on ReferenceId — idempotency enforcement at the DB level
            entity.HasIndex(p => p.ReferenceId)
                  .IsUnique()
                  .HasDatabaseName("IX_Payments_ReferenceId");

            // Index on Status for filtering queries
            entity.HasIndex(p => p.Status)
                  .HasDatabaseName("IX_Payments_Status");

            // Index on CreatedAt for time-range queries and ordering
            entity.HasIndex(p => p.CreatedAt)
                  .HasDatabaseName("IX_Payments_CreatedAt");

            // Decimal precision
            entity.Property(p => p.Amount)
                  .HasPrecision(18, 2);
        });

        modelBuilder.Entity<PaymentEvent>(entity =>
        {
            entity.HasKey(e => e.Id);

            entity.Property(e => e.Id)
                  .ValueGeneratedOnAdd();

            // Index on PaymentId for retrieving event history
            entity.HasIndex(e => e.PaymentId)
                  .HasDatabaseName("IX_PaymentEvents_PaymentId");

            // Index on OccurredAt for chronological ordering
            entity.HasIndex(e => e.OccurredAt)
                  .HasDatabaseName("IX_PaymentEvents_OccurredAt");
        });
    }
}
