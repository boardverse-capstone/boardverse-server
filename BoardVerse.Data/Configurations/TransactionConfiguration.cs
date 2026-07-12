using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations
{
    public class TransactionConfiguration : IEntityTypeConfiguration<Transaction>
    {
        public void Configure(EntityTypeBuilder<Transaction> builder)
        {
            builder.ToTable("Transactions");
            builder.HasKey(t => t.Id);
            builder.Property(t => t.Id).ValueGeneratedNever();

            builder.Property(t => t.UserId);
            builder.Property(t => t.Amount).HasPrecision(18, 2).IsRequired();
            builder.Property(t => t.Currency).HasMaxLength(10).IsRequired();
            builder.Property(t => t.Gateway).HasMaxLength(100).IsRequired();
            builder.Property(t => t.GatewayTransactionId).HasMaxLength(200);
            builder.Property(t => t.GatewayResponseCode).HasMaxLength(50);
            builder.Property(t => t.GatewayResponseMessage).HasMaxLength(500);
            builder.Property(t => t.Status)
                .HasConversion<int>()
                .IsRequired();
            builder.Property(t => t.Type)
                .HasConversion<int>()
                .IsRequired();
            builder.Property(t => t.CreatedAt).IsRequired();
            builder.Property(t => t.CompletedAt);

            builder.HasOne(t => t.User)
                .WithMany()
                .HasForeignKey(t => t.UserId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.HasIndex(t => t.GatewayTransactionId);
            builder.HasIndex(t => new { t.Status, t.Type });
        }
    }
}
