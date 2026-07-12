using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations;

public class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
{
    public void Configure(EntityTypeBuilder<Transaction> entity)
    {
        entity.ToTable("Transactions");

        entity.HasKey(t => t.Id);

        entity.Property(t => t.Id)
            .ValueGeneratedNever();

        entity.Property(t => t.Amount)
            .HasPrecision(18, 2);

        entity.Property(t => t.Currency)
            .HasMaxLength(10)
            .HasDefaultValue("VND");

        entity.Property(t => t.Gateway)
            .HasMaxLength(50);

        entity.Property(t => t.GatewayTransactionId)
            .HasMaxLength(100);

        entity.Property(t => t.GatewayResponseCode)
            .HasMaxLength(50);

        entity.Property(t => t.GatewayResponseMessage)
            .HasMaxLength(500);

        entity.Property(t => t.Status)
            .HasConversion<int>();

        entity.Property(t => t.Type)
            .HasConversion<int>();

        entity.Property(t => t.Direction)
            .HasConversion<int>();

        entity.Property(t => t.FromAccount)
            .HasMaxLength(50);

        entity.Property(t => t.ToAccount)
            .HasMaxLength(50);

        entity.Property(t => t.Notes)
            .HasMaxLength(500);

        entity.Property(t => t.CreatedAt)
            .HasDefaultValueSql("NOW()");

        // Indexes
        entity.HasIndex(t => t.GatewayTransactionId);
        entity.HasIndex(t => t.Status);
        entity.HasIndex(t => t.Type);
        entity.HasIndex(t => t.UserId);
        entity.HasIndex(t => t.CafeId);

        // Relationships
        entity.HasOne(t => t.User)
            .WithMany()
            .HasForeignKey(t => t.UserId)
            .OnDelete(DeleteBehavior.SetNull);

        entity.HasOne(t => t.Cafe)
            .WithMany()
            .HasForeignKey(t => t.CafeId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
