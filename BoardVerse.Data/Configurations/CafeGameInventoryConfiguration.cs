using BoardVerse.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations
{
    public class CafeGameInventoryConfiguration : IEntityTypeConfiguration<CafeGameInventory>
    {
        public void Configure(EntityTypeBuilder<CafeGameInventory> builder)
        {
            builder.HasKey(i => i.Id);
            builder.Property(i => i.Id).ValueGeneratedNever();
            builder.Property(i => i.CafeId).IsRequired();
            builder.Property(i => i.GameTemplateId).IsRequired();
            builder.Property(i => i.BoxQuantity).IsRequired();
            builder.Property(i => i.Status)
                .HasConversion<string>()
                .HasMaxLength(50)
                .IsRequired();
            builder.Property(i => i.CreatedAt).IsRequired();
            builder.Property(i => i.UpdatedAt).IsRequired();
            builder.Property(i => i.IsActive).IsRequired().HasDefaultValue(true);

            builder.HasOne(i => i.Cafe)
                .WithMany()
                .HasForeignKey(i => i.CafeId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(i => i.GameTemplate)
                .WithMany()
                .HasForeignKey(i => i.GameTemplateId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasIndex(i => new { i.CafeId, i.GameTemplateId })
                .IsUnique()
                .HasFilter("\"IsActive\" = true");
        }
    }
}
