using BoardVerse.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations;

public class DeviceTokenConfiguration : IEntityTypeConfiguration<DeviceToken>
{
    public void Configure(EntityTypeBuilder<DeviceToken> entity)
    {
        entity.HasKey(t => t.Id);
        entity.Property(t => t.Id).ValueGeneratedNever();

        entity.Property(t => t.UserId).IsRequired();
        entity.Property(t => t.Token).IsRequired().HasMaxLength(512);
        entity.Property(t => t.Platform).IsRequired().HasMaxLength(16);
        entity.Property(t => t.AppVersion).HasMaxLength(32);
        entity.Property(t => t.DeviceModel).HasMaxLength(128);
        entity.Property(t => t.CreatedAt).IsRequired();
        entity.Property(t => t.IsInvalidated).IsRequired();

        // FCM token phải unique (1 thiết bị chỉ có 1 row tránh duplicate push).
        entity.HasIndex(t => t.Token).IsUnique();

        // Lookup theo userId cho notification fan-out.
        entity.HasIndex(t => t.UserId);

        // Filter active tokens (IsInvalidated=false) cho query nhanh.
        entity.HasIndex(t => new { t.UserId, t.IsInvalidated });
    }
}
