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

            builder.HasOne(c => c.Manager)
                .WithMany()
                .HasForeignKey(c => c.ManagerId)
                .OnDelete(DeleteBehavior.Restrict);

            builder.HasIndex(c => c.ManagerId);

            builder.HasIndex(c => c.Location)
                .HasMethod("GIST");
        }
    }
}
