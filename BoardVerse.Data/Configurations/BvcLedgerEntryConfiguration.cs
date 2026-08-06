using BoardVerse.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations;

public class BvcLedgerEntryConfiguration : IEntityTypeConfiguration<BvcLedgerEntry>
{
    public void Configure(EntityTypeBuilder<BvcLedgerEntry> builder)
    {
        builder.ToTable("BvcLedgerEntries");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.Type)
            .HasConversion<int>();

        builder.Property(e => e.Amount)
            .IsRequired();

        builder.Property(e => e.IdempotencyKey)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(e => e.RelatedPaymentRef)
            .HasMaxLength(256);

        builder.Property(e => e.Note)
            .HasMaxLength(512);

        builder.Property(e => e.CreatedAt)
            .HasDefaultValueSql("now() at time zone 'utc'");

        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Restrict); // C4: BVC ledger là append-only (BR § III.3); KHÔNG cascade delete.

        // BR § III.3 + § XVII.1 — Idempotency key phải UNIQUE.
        builder.HasIndex(e => e.IdempotencyKey)
            .IsUnique()
            .HasDatabaseName("UX_BvcLedgerEntries_IdempotencyKey");

        builder.HasIndex(e => new { e.UserId, e.CreatedAt })
            .HasDatabaseName("IX_BvcLedgerEntries_UserId_CreatedAt")
            .IsDescending(false, true);

        builder.HasIndex(e => e.RelatedLobbyId)
            .HasDatabaseName("IX_BvcLedgerEntries_RelatedLobbyId");

        builder.HasIndex(e => e.RelatedBookingId)
            .HasDatabaseName("IX_BvcLedgerEntries_RelatedBookingId");
    }
}
