using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations
{
    public class KarmaLogConfiguration : IEntityTypeConfiguration<KarmaLog>
    {
        public void Configure(EntityTypeBuilder<KarmaLog> entity)
        {
            entity.HasKey(k => k.Id);
            entity.Property(k => k.Id).ValueGeneratedNever();
            entity.Property(k => k.ViolationCategory).HasConversion<string>().HasMaxLength(50).IsRequired();
            entity.Property(k => k.Source).HasConversion<string>().HasMaxLength(50).IsRequired();
            entity.Property(k => k.DeltaAmount).HasColumnType("numeric(8,2)").IsRequired();
            entity.Property(k => k.Reason).IsRequired().HasMaxLength(1000);
            entity.Property(k => k.CreatedAt).IsRequired();

            entity.HasIndex(k => k.UserId);
            entity.HasIndex(k => k.CreatedAt);
            entity.HasIndex(k => new { k.UserId, k.ViolationCategory });

            entity.HasOne(k => k.User)
                .WithMany()
                .HasForeignKey(k => k.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(k => k.ActorUser)
                .WithMany()
                .HasForeignKey(k => k.ActorUserId)
                .OnDelete(DeleteBehavior.SetNull);
        }
    }
}
