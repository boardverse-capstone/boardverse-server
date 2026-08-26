using BoardVerse.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations;

/// <summary>
/// Configuration cho entity SessionExtensionRequest (Player Session API GAP-1).
/// Mỗi request có thể được approve, reject hoặc expire.
/// </summary>
public class SessionExtensionRequestConfiguration : IEntityTypeConfiguration<SessionExtensionRequest>
{
    public void Configure(EntityTypeBuilder<SessionExtensionRequest> builder)
    {
        builder.ToTable("SessionExtensionRequests");

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();

        builder.Property(r => r.SessionId).IsRequired();
        builder.Property(r => r.RequestedByUserId).IsRequired();
        builder.Property(r => r.RequestedMinutes).IsRequired();
        builder.Property(r => r.EstimatedAdditionalCostVnd)
            .IsRequired()
            .HasPrecision(18, 2);
        builder.Property(r => r.Status).HasConversion<int>().IsRequired();
        builder.Property(r => r.CreatedAt).IsRequired();
        builder.Property(r => r.RejectionReason).HasMaxLength(500);

        // FK về ActiveSession — Restrict đểo không cascade xóa session khi cần audit.
        builder.HasOne(r => r.Session)
            .WithMany()
            .HasForeignKey(r => r.SessionId)
            .OnDelete(DeleteBehavior.Restrict);

        // FK về User (player request).
        builder.HasOne(r => r.RequestedByUser)
            .WithMany()
            .HasForeignKey(r => r.RequestedByUserId)
            .OnDelete(DeleteBehavior.Restrict);

        // Index phục vụ background job expiry query:
        // WHERE Status = Pending AND CreatedAt < cutoff ORDER BY CreatedAt LIMIT 500
        builder.HasIndex(r => new { r.Status, r.CreatedAt });
        builder.HasIndex(r => r.SessionId);
        builder.HasIndex(r => r.RequestedByUserId);
    }
}