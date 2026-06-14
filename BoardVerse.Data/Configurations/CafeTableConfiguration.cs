using BoardVerse.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations
{
    public class CafeTableConfiguration : IEntityTypeConfiguration<CafeTable>
    {
        public void Configure(EntityTypeBuilder<CafeTable> builder)
        {
            builder.ToTable("CafeTables");

            builder.HasKey(t => t.Id);
            builder.Property(t => t.Id).ValueGeneratedNever();
            builder.Property(t => t.CafeId).IsRequired();
            builder.Property(t => t.Name).IsRequired().HasMaxLength(100);
            builder.Property(t => t.SortOrder).IsRequired();
            builder.Property(t => t.Status)
                .HasConversion<string>()
                .HasMaxLength(20)
                .IsRequired();
            builder.Property(t => t.CreatedAt).IsRequired();
            builder.Property(t => t.IsActive).IsRequired().HasDefaultValue(true);

            builder.HasOne(t => t.Cafe)
                .WithMany(c => c.Tables)
                .HasForeignKey(t => t.CafeId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(t => new { t.CafeId, t.Name })
                .IsUnique()
                .HasFilter("\"IsActive\" = true");

            builder.HasIndex(t => new { t.CafeId, t.Status })
                .HasFilter("\"IsActive\" = true");
        }
    }
}
