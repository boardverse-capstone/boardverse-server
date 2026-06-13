using BoardVerse.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations
{
    public class CategoryConfiguration : IEntityTypeConfiguration<Category>
    {
        public static readonly Guid HiddenRoleId = new("c1111111-1111-1111-1111-111111111111");
        public static readonly Guid StrategyId = new("c1111111-1111-1111-1111-111111111112");
        public static readonly Guid PartyId = new("c1111111-1111-1111-1111-111111111113");
        public static readonly Guid CooperativeId = new("c1111111-1111-1111-1111-111111111114");
        public static readonly Guid CompetitiveId = new("c1111111-1111-1111-1111-111111111115");
        public static readonly Guid AdventureId = new("c1111111-1111-1111-1111-111111111116");

        public void Configure(EntityTypeBuilder<Category> builder)
        {
            builder.HasKey(c => c.Id);
            builder.Property(c => c.Id).ValueGeneratedNever();
            builder.Property(c => c.Name).IsRequired().HasMaxLength(100);
            builder.Property(c => c.Slug).IsRequired().HasMaxLength(100);
            builder.Property(c => c.Description).HasMaxLength(500);
            builder.Property(c => c.SortOrder).IsRequired();
            builder.Property(c => c.IsActive).IsRequired().HasDefaultValue(true);
            builder.Property(c => c.CreatedAt).IsRequired();
            builder.Property(c => c.UpdatedAt).IsRequired();

            builder.HasIndex(c => c.Slug).IsUnique();

            var seedDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            builder.HasData(
                new Category
                {
                    Id = HiddenRoleId,
                    Name = "Ẩn vai",
                    Slug = "an-vai",
                    Description = "Trò chơi suy luận vai trò bí mật",
                    SortOrder = 1,
                    IsActive = true,
                    CreatedAt = seedDate,
                    UpdatedAt = seedDate
                },
                new Category
                {
                    Id = StrategyId,
                    Name = "Chiến thuật",
                    Slug = "chien-thuat",
                    Description = "Tư duy chiến lược, tối ưu nguồn lực và điểm số",
                    SortOrder = 2,
                    IsActive = true,
                    CreatedAt = seedDate,
                    UpdatedAt = seedDate
                },
                new Category
                {
                    Id = PartyId,
                    Name = "Giải trí",
                    Slug = "giai-tri",
                    Description = "Nhẹ nhàng, vui vẻ, phù hợp tụ tập đông người",
                    SortOrder = 3,
                    IsActive = true,
                    CreatedAt = seedDate,
                    UpdatedAt = seedDate
                },
                new Category
                {
                    Id = CooperativeId,
                    Name = "Hợp tác",
                    Slug = "hop-tac",
                    Description = "Người chơi cùng phối hợp để đạt mục tiêu chung",
                    SortOrder = 4,
                    IsActive = true,
                    CreatedAt = seedDate,
                    UpdatedAt = seedDate
                },
                new Category
                {
                    Id = CompetitiveId,
                    Name = "Đối kháng",
                    Slug = "doi-khang",
                    Description = "Cạnh tranh trực tiếp giữa các người chơi",
                    SortOrder = 5,
                    IsActive = true,
                    CreatedAt = seedDate,
                    UpdatedAt = seedDate
                },
                new Category
                {
                    Id = AdventureId,
                    Name = "Phiêu lưu",
                    Slug = "phieu-luu",
                    Description = "Khám phá cốt truyện và thế giới trong game",
                    SortOrder = 6,
                    IsActive = true,
                    CreatedAt = seedDate,
                    UpdatedAt = seedDate
                }
            );
        }
    }
}
