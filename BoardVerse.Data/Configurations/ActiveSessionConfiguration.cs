using BoardVerse.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations
{
    public class ActiveSessionConfiguration : IEntityTypeConfiguration<ActiveSession>
    {
        public void Configure(EntityTypeBuilder<ActiveSession> builder)
        {
            builder.ToTable("ActiveSessions");

            builder.HasKey(s => s.Id);
            builder.Property(s => s.Id).ValueGeneratedNever();
            builder.Property(s => s.CafeId).IsRequired();
            builder.Property(s => s.CafeTableId).IsRequired();
            builder.Property(s => s.CafeInventoryBoxId).IsRequired();
            builder.Property(s => s.GameTemplateId).IsRequired();
            builder.Property(s => s.StartedAt).IsRequired();
            builder.Property(s => s.IsActive).IsRequired().HasDefaultValue(true);
            builder.Property(s => s.CreatedAt).IsRequired();

            builder.HasOne(s => s.Cafe)
                .WithMany()
                .HasForeignKey(s => s.CafeId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(s => s.CafeTable)
                .WithMany()
                .HasForeignKey(s => s.CafeTableId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(s => s.CafeInventoryBox)
                .WithMany()
                .HasForeignKey(s => s.CafeInventoryBoxId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(s => s.GameTemplate)
                .WithMany()
                .HasForeignKey(s => s.GameTemplateId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasIndex(s => s.CafeInventoryBoxId)
                .IsUnique()
                .HasFilter("\"IsActive\" = true");

            builder.HasIndex(s => new { s.CafeId, s.GameTemplateId, s.IsActive });
        }
    }
}
