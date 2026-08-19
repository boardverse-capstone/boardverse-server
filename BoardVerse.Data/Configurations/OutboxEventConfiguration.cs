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

        // Indexes phục vụ worker poll.
        builder.HasIndex(e => new { e.Processed, e.CreatedAt })
            .HasDatabaseName("IX_OutboxEvents_Processed_CreatedAt");

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