using BoardVerse.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations
{
    public class SystemConfigurationConfiguration : IEntityTypeConfiguration<SystemConfiguration>
    {
        public void Configure(EntityTypeBuilder<SystemConfiguration> entity)
        {
            entity.HasKey(c => c.ConfigKey);
            entity.Property(c => c.ConfigKey).HasMaxLength(100);
            entity.Property(c => c.ConfigValue).IsRequired().HasMaxLength(500);
            entity.Property(c => c.Description).IsRequired().HasMaxLength(1000);
            entity.Property(c => c.UpdatedAt).IsRequired();
        }
    }
}
