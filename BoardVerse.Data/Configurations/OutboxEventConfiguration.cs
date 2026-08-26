using BoardVerse.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations;

/// <summary>
/// EF configuration cho <see cref="OutboxEvent"/> (BR-REQUIRED §17.5 Transactional Outbox).
/// </summary>
public class OutboxEventConfiguration : IEntityTypeConfiguration<OutboxEvent>
{
    public void Configure(EntityTypeBuilder<OutboxEvent> builder)
    {
        builder.ToTable("OutboxEvents");
        builder.HasKey(e => e.Id);

        builder.Property(e => e.Id)
            .ValueGeneratedNever();

        builder.Property(e => e.EventType)
            .HasConversion<int>()
            .IsRequired();

        builder.Property(e => e.Payload)
            .HasColumnType("jsonb")
            .IsRequired();

        builder.Property(e => e.IdempotencyKey)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(e => e.CreatedAt)
            .IsRequired();

        builder.Property(e => e.Processed)
            .HasDefaultValue(false);

        builder.Property(e => e.ProcessedAt);

        builder.Property(e => e.RetryCount)
            .HasDefaultValue(0);

        builder.Property(e => e.LastError)
            .HasMaxLength(2000);

        /// <summary>
        /// GAP-R4-A8 Fix: Next retry timestamp (exponential backoff).
        /// Event chỉ được fetch khi <c>NextRetryAt &lt;= now()</c> hoặc null.
        /// Backoff: 10s × 2^(retry-1), capped 300s.
        /// </summary>
        builder.Property(e => e.NextRetryAt);

        // Indexes phục vụ worker poll.
        builder.HasIndex(e => new { e.Processed, e.CreatedAt })
            .HasDatabaseName("IX_OutboxEvents_Processed_CreatedAt");

        // GAP-R4-A8 Fix: Index phụ để filter theo NextRetryAt
        // (worker query: Processed=false AND (NextRetryAt IS NULL OR NextRetryAt <= now())).
        builder.HasIndex(e => e.NextRetryAt)
            .HasDatabaseName("IX_OutboxEvents_NextRetryAt");

        builder.HasIndex(e => e.IdempotencyKey)
            .IsUnique()
            .HasDatabaseName("UX_OutboxEvents_IdempotencyKey");

        // FK optional.
        builder.HasOne(e => e.Reservation)
            .WithMany()
            .HasForeignKey(e => e.ReservationId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}