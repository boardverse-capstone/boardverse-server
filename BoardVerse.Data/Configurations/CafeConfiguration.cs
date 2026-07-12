using BoardVerse.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations
{
    public class CafeConfiguration : IEntityTypeConfiguration<Cafe>
    {
        public void Configure(EntityTypeBuilder<Cafe> builder)
        {
            builder.HasKey(c => c.Id);

            builder.Property(c => c.Id)
                .ValueGeneratedNever();

            builder.Property(c => c.Name)
                .IsRequired()
                .HasMaxLength(200);

            builder.Property(c => c.Address)
                .IsRequired()
                .HasMaxLength(500);

            builder.Property(c => c.Latitude)
                .HasColumnType("double precision");

            builder.Property(c => c.Longitude)
                .HasColumnType("double precision");

            builder.Property(c => c.Location)
                .HasColumnType("geography (point,4326)");

            builder.Property(c => c.PhoneNumber)
                .HasMaxLength(50);

            builder.Property(c => c.Description)
                .HasMaxLength(1000);

            builder.Property(c => c.CreatedAt)
                .IsRequired();

            builder.Property(c => c.IsActive)
                .IsRequired()
                .HasDefaultValue(true);

            builder.Property(c => c.PartnerOperationalStatus)
                .HasConversion<string>()
                .HasMaxLength(20);

            builder.Property(c => c.PartnerOperationalStatusReason)
                .HasMaxLength(500);

            builder.Property(c => c.SpaceImageUrlsJson)
                .IsRequired()
                .HasDefaultValue("[]");

            builder.Property(c => c.TableLayoutJson)
                .IsRequired()
                .HasDefaultValue("[]");

            builder.Property(c => c.PopularGamesList)
                .IsRequired()
                .HasMaxLength(2000)
                .HasDefaultValue(string.Empty);

            builder.Property(c => c.BillingModel)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired()
                .HasDefaultValue(Core.Enum.CafePartnerBillingModel.TimeBased);

            builder.Property(c => c.BasePrice)
                .HasColumnType("numeric(18,2)")
                .IsRequired();

            builder.Property(c => c.TieredBlockRate)
                .HasColumnType("numeric(18,2)");

            builder.Property(c => c.TieredBlockMinutes)
                .IsRequired();

            builder.Property(c => c.DepositPercentage)
                .HasColumnType("numeric(4,4)")
                .IsRequired();

            builder.Property(c => c.IsPricingLocked)
                .IsRequired();

            // SePay Configuration (Session Payment)
            builder.Property(c => c.SePayMerchantId)
                .HasMaxLength(100);

            builder.Property(c => c.SePayApiKey)
                .HasMaxLength(200);

            builder.Property(c => c.SePaySecretKey)
                .HasMaxLength(200);

            builder.Property(c => c.SePayReturnUrl)
                .HasMaxLength(500);

            builder.Property(c => c.SePayBankCode)
                .HasMaxLength(50);

            builder.Property(c => c.SePayAccountNumber)
                .HasMaxLength(50);

            builder.HasOne(c => c.Manager)
                .WithMany()
                .HasForeignKey(c => c.ManagerId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(c => c.PartnerApplication)
                .WithOne(a => a.CreatedCafe)
                .HasForeignKey<CafePartnerApplication>(a => a.CreatedCafeId)
                .OnDelete(DeleteBehavior.SetNull);

            builder.HasIndex(c => c.ManagerId);

            builder.HasIndex(c => c.Location)
                .HasMethod("GIST");
        }
    }
}
