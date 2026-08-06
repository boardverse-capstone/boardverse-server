using BoardVerse.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations;

public class BvcTopUpRequestConfiguration : IEntityTypeConfiguration<BvcTopUpRequest>
{
    public void Configure(EntityTypeBuilder<BvcTopUpRequest> builder)
    {
        builder.ToTable("BvcTopUpRequests");

        builder.HasKey(e => e.Id);

        builder.Property(e => e.OrderId)
            .HasMaxLength(64)
            .IsRequired();

        builder.Property(e => e.IdempotencyKey)
            .HasMaxLength(128)
            .IsRequired();

        builder.Property(e => e.GatewayTransactionId)
            .HasMaxLength(128);

        builder.Property(e => e.Status)
            .HasConversion<int>();

        builder.Property(e => e.CreatedAt)
            .HasDefaultValueSql("now() at time zone 'utc'");

        builder.HasOne(e => e.User)
            .WithMany()
            .HasForeignKey(e => e.UserId)
            .OnDelete(DeleteBehavior.Restrict); // H1: Top-up requests mang OrderId/IdempotencyKey/GatewayTxnId cho audit.

        // BR § XVII.1: OrderId + IdempotencyKey phải UNIQUE.
        builder.HasIndex(e => e.OrderId)
            .IsUnique()
            .HasDatabaseName("UX_BvcTopUpRequests_OrderId");

        builder.HasIndex(e => e.IdempotencyKey)
            .IsUnique()
            .HasDatabaseName("UX_BvcTopUpRequests_IdempotencyKey");

        builder.HasIndex(e => new { e.UserId, e.CreatedAt })
            .HasDatabaseName("IX_BvcTopUpRequests_UserId_CreatedAt")
            .IsDescending(false, true);

        builder.HasIndex(e => e.Status)
            .HasDatabaseName("IX_BvcTopUpRequests_Status");
    }
}
