using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations
{
    public class BookingDepositConfiguration : IEntityTypeConfiguration<BookingDeposit>
    {
    public void Configure(EntityTypeBuilder<BookingDeposit> builder)
    {
        builder.ToTable("BookingDeposits");

        builder.HasKey(d => d.Id);
        builder.Property(d => d.Id).ValueGeneratedNever();
        builder.Property(d => d.OrderId).IsRequired().HasMaxLength(50);
        builder.Property(d => d.ActiveSessionId);
        builder.Property(d => d.CafeId).IsRequired();
        builder.Property(d => d.CafeManagerId).IsRequired();
        builder.Property(d => d.Amount).IsRequired();
        builder.Property(d => d.Status).HasConversion<int>().IsRequired();
        builder.Property(d => d.RefundPolicy).HasConversion<int>().IsRequired();
        builder.Property(d => d.TransferContent).HasMaxLength(100);
        builder.Property(d => d.SePayTransactionId).HasMaxLength(100);
        builder.Property(d => d.SePayTransferId).HasMaxLength(100);
        builder.Property(d => d.CreatedAt).IsRequired();
        builder.Property(d => d.ScheduledAt);

        builder.HasOne(d => d.MasterAccount)
            .WithMany()
            .HasForeignKey(d => d.MasterAccountId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasOne(d => d.Cafe)
            .WithMany()
            .HasForeignKey(d => d.CafeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(d => d.ActiveSession)
            .WithMany()
            .HasForeignKey(d => d.ActiveSessionId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.HasIndex(d => d.OrderId).IsUnique();
        builder.HasIndex(d => d.ActiveSessionId).HasFilter("\"ActiveSessionId\" IS NOT NULL");
        builder.HasIndex(d => new { d.CafeId, d.Status });
        builder.HasIndex(d => d.SePayTransactionId).HasFilter("\"SePayTransactionId\" IS NOT NULL");
    }
    }
}
