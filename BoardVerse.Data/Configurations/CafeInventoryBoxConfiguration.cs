using BoardVerse.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations
{
    public class CafeInventoryBoxConfiguration : IEntityTypeConfiguration<CafeInventoryBox>
    {
        public void Configure(EntityTypeBuilder<CafeInventoryBox> builder)
        {
            builder.ToTable("CafeInventoryBoxes");

            builder.HasKey(b => b.Id);
            builder.Property(b => b.Id).ValueGeneratedNever();
            builder.Property(b => b.CafeGameInventoryId).IsRequired();
            builder.Property(b => b.Barcode).IsRequired().HasMaxLength(50);
            builder.Property(b => b.Status)
                .HasConversion<string>()
                .HasMaxLength(50)
                .IsRequired();
            builder.Property(b => b.CreatedAt).IsRequired();
            builder.Property(b => b.IsActive).IsRequired().HasDefaultValue(true);

            builder.HasOne(b => b.CafeGameInventory)
                .WithMany(i => i.Boxes)
                .HasForeignKey(b => b.CafeGameInventoryId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(b => b.Barcode)
                .IsUnique()
                .HasFilter("\"IsActive\" = true");

            builder.HasIndex(b => new { b.CafeGameInventoryId, b.Status })
                .HasFilter("\"IsActive\" = true");
        }
    }
}
