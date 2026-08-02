using BoardVerse.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations;

public class WalletConfiguration : IEntityTypeConfiguration<Wallet>
{
    public void Configure(EntityTypeBuilder<Wallet> builder)
    {
        builder.ToTable("Wallets");

        builder.HasKey(w => w.UserId);

        builder.Property(w => w.AvailableBalance)
            .HasDefaultValue(0);

        builder.Property(w => w.HeldBalance)
            .HasDefaultValue(0);

        builder.Property(w => w.TotalActiveDeposit)
            .HasDefaultValue(0);

        builder.Property(w => w.RiskMultiplier)
            .HasColumnType("numeric(4,2)")
            .HasDefaultValue(1.0m);

        builder.Property(w => w.RiskScore)
            .HasDefaultValue(0);

        builder.Property(w => w.RiskLevel)
            .HasConversion<int>()
            .HasDefaultValue(BoardVerse.Core.Enum.RiskLevel.Low);

        builder.Property(w => w.IsCoolingOff)
            .HasDefaultValue(false);

        builder.Property(w => w.AccountStatus)
            .HasConversion<int>()
            .HasDefaultValue(BoardVerse.Core.Enum.AccountStatus.Active);

        builder.Property(w => w.CreatedAt)
            .HasDefaultValueSql("now() at time zone 'utc'");

        builder.Property(w => w.UpdatedAt)
            .HasDefaultValueSql("now() at time zone 'utc'");

        builder.HasOne(w => w.User)
            .WithMany()
            .HasForeignKey(w => w.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(w => w.AccountStatus)
            .HasDatabaseName("IX_Wallets_AccountStatus");
    }
}
