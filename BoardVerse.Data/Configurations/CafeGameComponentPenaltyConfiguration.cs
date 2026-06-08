using BoardVerse.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations
{
    public class CafeGameComponentPenaltyConfiguration : IEntityTypeConfiguration<CafeGameComponentPenalty>
    {
        public void Configure(EntityTypeBuilder<CafeGameComponentPenalty> builder)
        {
            builder.HasKey(p => p.Id);
            builder.Property(p => p.Id).ValueGeneratedNever();
            builder.Property(p => p.CafeGameInventoryId).IsRequired();
            builder.Property(p => p.GameComponentTemplateId).IsRequired();
            builder.Property(p => p.PenaltyFee).HasPrecision(18, 2).IsRequired();
            builder.Property(p => p.CreatedAt).IsRequired();
            builder.Property(p => p.UpdatedAt).IsRequired();

            builder.HasOne(p => p.CafeGameInventory)
                .WithMany(i => i.ComponentPenalties)
                .HasForeignKey(p => p.CafeGameInventoryId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(p => p.GameComponentTemplate)
                .WithMany()
                .HasForeignKey(p => p.GameComponentTemplateId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasIndex(p => new { p.CafeGameInventoryId, p.GameComponentTemplateId })
                .IsUnique();
        }
    }
}
