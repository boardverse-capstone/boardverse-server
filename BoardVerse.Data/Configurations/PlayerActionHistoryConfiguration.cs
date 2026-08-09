using BoardVerse.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations
{
    /// <summary>
    /// Cấu hình cho PlayerActionHistory — audit log theo BR-RISK-05.
    /// Metadata lưu dưới dạng JSONB để query/snapshot tiện hơn.
    /// </summary>
    public class PlayerActionHistoryConfiguration : IEntityTypeConfiguration<PlayerActionHistory>
    {
        public void Configure(EntityTypeBuilder<PlayerActionHistory> entity)
        {
            entity.HasKey(p => p.Id);
            entity.Property(p => p.Id).ValueGeneratedNever();

            entity.Property(p => p.ActionType)
                .HasConversion<string>()
                .HasMaxLength(50)
                .IsRequired();

            entity.Property(p => p.ActionBy).IsRequired();
            entity.Property(p => p.UserId).IsRequired();
            entity.Property(p => p.Reason).IsRequired().HasMaxLength(2000);
            entity.Property(p => p.Metadata).HasColumnType("jsonb");
            entity.Property(p => p.CreatedAt).IsRequired();

            entity.HasIndex(p => p.UserId);
            entity.HasIndex(p => p.CreatedAt);
            entity.HasIndex(p => p.ActionType);
        }
    }
}
