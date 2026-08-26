using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations
{
    public class CafeSettlementConfiguration : IEntityTypeConfiguration<CafeSettlement>
    {
        public void Configure(EntityTypeBuilder<CafeSettlement> builder)
        {
            builder.ToTable("CafeSettlements");

            builder.HasKey(s => s.Id);
            builder.Property(s => s.Id).ValueGeneratedNever();
            builder.Property(s => s.CafeId).IsRequired();
            builder.Property(s => s.CafeManagerId).IsRequired();
            builder.Property(s => s.DepositAmount).IsRequired().HasColumnType("numeric(18,2)");
            builder.Property(s => s.FeeAmount).HasColumnType("numeric(18,2)");
            builder.Property(s => s.NetTransferAmount).IsRequired().HasColumnType("numeric(18,2)");
            builder.Property(s => s.Status).HasConversion<int>().IsRequired();
            builder.Property(s => s.SePayTransferId).HasMaxLength(100);
            builder.Property(s => s.FailureReason).HasMaxLength(500);
            builder.Property(s => s.CreatedAt).IsRequired();

            // H3: Concurrency token for financial payout (prevent double-process from concurrent webhooks).
            builder.Property(s => s.UpdatedAt)
                .HasDefaultValueSql("now() at time zone 'utc'")
                .IsConcurrencyToken();

            builder.HasIndex(s => new { s.CafeId, s.Status });
            builder.HasIndex(s => s.ActiveSessionId);
            builder.HasIndex(s => s.BookingDepositId);
            builder.HasIndex(s => s.CafeManagerId).HasDatabaseName("IX_CafeSettlements_CafeManagerId"); // L5

            // W-06: Tối ưu GET /admin/settlements/failed sort UpdatedAt DESC.
            builder.HasIndex(s => new { s.Status, s.UpdatedAt })
                .HasDatabaseName("IX_CafeSettlements_Status_UpdatedAt");
        }
    }
}
