using BoardVerse.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations
{
    public class PaymentMasterAccountConfiguration : IEntityTypeConfiguration<PaymentMasterAccount>
    {
        public void Configure(EntityTypeBuilder<PaymentMasterAccount> builder)
        {
            builder.ToTable("PaymentMasterAccounts");

            builder.HasKey(m => m.Id);
            builder.Property(m => m.Id).ValueGeneratedNever();
            builder.Property(m => m.Provider).HasMaxLength(100).IsRequired();
            builder.Property(m => m.AccountHolder).HasMaxLength(200).IsRequired();
            builder.Property(m => m.BankCode).HasMaxLength(10).IsRequired();
            builder.Property(m => m.MaskedAccountNumber).HasMaxLength(50).IsRequired();
            builder.Property(m => m.VirtualAccountNumber).HasMaxLength(50);
            builder.Property(m => m.QrContent).HasMaxLength(1000);
            builder.Property(m => m.WebhookSecret).HasMaxLength(500);

            builder.HasIndex(m => new { m.IsActive, m.Provider }).HasFilter("\"IsActive\" = true");
        }
    }
}
