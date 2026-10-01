using BoardVerse.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations;

/// <summary>
/// EF Core configuration cho <see cref="ForceCloseAuditLog"/>.
/// M2/C2.16 — docs/design/host-deposit-discount-and-bvc-payment-design.md §C2.16.
/// (2026-10-01)
/// </summary>
public class ForceCloseAuditLogConfiguration : IEntityTypeConfiguration<ForceCloseAuditLog>
{
    public void Configure(EntityTypeBuilder<ForceCloseAuditLog> builder)
    {
        builder.ToTable("ForceCloseAuditLogs");

        builder.HasKey(l => l.Id);
        builder.Property(l => l.Id).ValueGeneratedNever();

        builder.Property(l => l.SessionId).IsRequired();
        builder.Property(l => l.TriggeredByUserId).IsRequired();

        builder.Property(l => l.UnpaidHandling)
            .HasMaxLength(30)
            .IsRequired();

        builder.Property(l => l.Reason)
            .HasMaxLength(500)
            .IsRequired();

        builder.Property(l => l.CreatedAt)
            .HasColumnType("timestamp with time zone")
            .HasDefaultValueSql("now() at time zone 'utc'");

        // UnpaidMemberIds list được map sang Postgres UUID[] column.
        // Use built-in List<Guid> mapping (Npgsql auto-detects).
        builder.Property(l => l.UnpaidMemberIds)
            .HasColumnType("uuid[]")
            .IsRequired();

        builder.Property(l => l.SessionClosedAfter)
            .IsRequired()
            .HasDefaultValue(false);

        // ===== Indexes cho query phổ biến =====
        builder.HasIndex(l => l.SessionId)
            .HasDatabaseName("IX_ForceCloseAuditLogs_SessionId");

        builder.HasIndex(l => l.TriggeredByUserId)
            .HasDatabaseName("IX_ForceCloseAuditLogs_TriggeredByUserId");

        builder.HasIndex(l => l.CreatedAt)
            .HasDatabaseName("IX_ForceCloseAuditLogs_CreatedAt");

        // ===== Relationships =====
        builder.HasOne(l => l.Session)
            .WithMany()
            .HasForeignKey(l => l.SessionId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(l => l.TriggeredByUser)
            .WithMany()
            .HasForeignKey(l => l.TriggeredByUserId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}