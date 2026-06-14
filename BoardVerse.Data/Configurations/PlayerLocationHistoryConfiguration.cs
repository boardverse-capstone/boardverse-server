using BoardVerse.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations
{
    public class PlayerLocationHistoryConfiguration : IEntityTypeConfiguration<PlayerLocationHistory>
    {
        public void Configure(EntityTypeBuilder<PlayerLocationHistory> builder)
        {
            builder.HasKey(h => h.Id);

            builder.Property(h => h.Id)
                .ValueGeneratedNever();

            builder.Property(h => h.Latitude)
                .HasColumnType("double precision");

            builder.Property(h => h.Longitude)
                .HasColumnType("double precision");

            builder.Property(h => h.Source)
                .HasConversion<string>()
                .HasMaxLength(20);

            builder.Property(h => h.RecordedAt)
                .IsRequired();

            builder.HasOne(h => h.User)
                .WithMany()
                .HasForeignKey(h => h.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasIndex(h => new { h.UserId, h.RecordedAt });
        }
    }
}
