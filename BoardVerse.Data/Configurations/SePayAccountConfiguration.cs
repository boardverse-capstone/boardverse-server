using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations;

public class SePayAccountConfiguration : IEntityTypeConfiguration<SePayAccount>
{
    public void Configure(EntityTypeBuilder<SePayAccount> builder)
    {
        builder.ToTable("SePayAccounts");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.AccountType)
            .HasConversion<int>()
            .IsRequired();

        builder.HasIndex(x => x.AccountType);

        builder.HasIndex(x => x.CafeId)
            .IsUnique()
            .HasFilter("\"AccountType\" = 1");

        builder.HasOne(x => x.Cafe)
            .WithOne(c => c.SePayAccount)
            .HasForeignKey<SePayAccount>(x => x.CafeId)
            .OnDelete(DeleteBehavior.SetNull);

        builder.Property(x => x.MerchantId)
            .HasMaxLength(100);

        builder.Property(x => x.ApiKey)
            .HasMaxLength(500);

        builder.Property(x => x.SecretKey)
            .HasMaxLength(500);

        builder.Property(x => x.WebhookToken)
            .HasMaxLength(500);

        builder.Property(x => x.ApiBaseUrl)
            .HasMaxLength(500);

        builder.Property(x => x.BankCode)
            .HasMaxLength(50);

        builder.Property(x => x.AccountNumber)
            .HasMaxLength(50);

        builder.Property(x => x.AccountHolder)
            .HasMaxLength(200);

        builder.Property(x => x.ReturnUrl)
            .HasMaxLength(500);

        builder.Property(x => x.Environment)
            .HasMaxLength(50);

        builder.Property(x => x.IsActive)
            .HasDefaultValue(true);

        // Audit columns
        builder.Property(x => x.CreatedByUserId);
        builder.Property(x => x.UpdatedByUserId);
    }
}
