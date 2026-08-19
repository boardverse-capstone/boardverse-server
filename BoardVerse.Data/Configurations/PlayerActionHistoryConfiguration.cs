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

            // DB schema hiện tại lưu ActionType dưới dạng integer (theo DB thật trên testing branch).
            // Code giữ enum, nhưng EF Core map sang int để khớp column đã migrate trước đó.
            // Lưu ý: nếu sau này muốn đổi sang string, phải tạo migration ALTER COLUMN ActionType TYPE varchar(50) trước.
            entity.Property(p => p.ActionType)
                .HasConversion<int>()
                .IsRequired();

            entity.Property(p => p.ActionBy).IsRequired();
            entity.Property(p => p.UserId).IsRequired();
            entity.Property(p => p.Reason).IsRequired().HasMaxLength(2000);
            entity.Property(p => p.Metadata).HasColumnType("jsonb");
            entity.Property(p => p.CreatedAt).IsRequired();

            entity.HasIndex(p => p.UserId);
            entity.HasIndex(p => p.CreatedAt);
            entity.HasIndex(p => p.ActionType);

            // Navigation đến User (target) — không có FK constraint cứng vì ActionBy có thể là Guid.Empty (system actor).
            // Sub-query ở repository vẫn lookup được Username dựa trên UserId FK thật trong DB.
            entity.HasOne(p => p.User)
                .WithMany()
                .HasForeignKey(p => p.UserId)
                .OnDelete(DeleteBehavior.Restrict);
        }
    }
}
