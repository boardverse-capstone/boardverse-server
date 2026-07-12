using BoardVerse.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations
{
    public class ActiveSessionGameConfiguration : IEntityTypeConfiguration<ActiveSessionGame>
    {
        public void Configure(EntityTypeBuilder<ActiveSessionGame> builder)
        {
            builder.ToTable("ActiveSessionGames");

            builder.HasKey(g => g.Id);
            builder.Property(g => g.Id).ValueGeneratedNever();
            builder.Property(g => g.ActiveSessionId).IsRequired();
            builder.Property(g => g.CafeInventoryBoxId).IsRequired();
            builder.Property(g => g.GameTemplateId).IsRequired();
            builder.Property(g => g.AttachedAt).IsRequired();
            builder.Property(g => g.CreatedAt).IsRequired();

            // BR-12: Component Checklist
            builder.Property(g => g.CheckStatus)
                .HasConversion<int>()
                .IsRequired();
            builder.Property(g => g.TotalPenaltyAmount)
                .HasPrecision(18, 2);

            builder.HasOne(g => g.ActiveSession)
                .WithMany(s => s.Games)
                .HasForeignKey(g => g.ActiveSessionId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(g => g.CafeInventoryBox)
                .WithMany()
                .HasForeignKey(g => g.CafeInventoryBoxId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasOne(g => g.GameTemplate)
                .WithMany()
                .HasForeignKey(g => g.GameTemplateId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasIndex(g => g.ActiveSessionId);
            builder.HasIndex(g => g.CafeInventoryBoxId);
        }
    }
}
