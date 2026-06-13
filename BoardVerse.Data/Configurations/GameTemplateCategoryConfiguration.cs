using BoardVerse.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations
{
    public class GameTemplateCategoryConfiguration : IEntityTypeConfiguration<GameTemplateCategory>
    {
        public void Configure(EntityTypeBuilder<GameTemplateCategory> builder)
        {
            builder.HasKey(gc => new { gc.GameTemplateId, gc.CategoryId });
            builder.Property(gc => gc.CreatedAt).IsRequired();

            builder.HasOne(gc => gc.GameTemplate)
                .WithMany(g => g.Categories)
                .HasForeignKey(gc => gc.GameTemplateId)
                .OnDelete(DeleteBehavior.Cascade);

            builder.HasOne(gc => gc.Category)
                .WithMany(c => c.GameTemplates)
                .HasForeignKey(gc => gc.CategoryId)
                .OnDelete(DeleteBehavior.Cascade);

            var seedDate = new DateTime(2024, 1, 1, 0, 0, 0, DateTimeKind.Utc);
            builder.HasData(
                // Catan — Chiến thuật, Đối kháng
                Link("11111111-1111-1111-1111-111111111111", CategoryConfiguration.StrategyId, seedDate),
                Link("11111111-1111-1111-1111-111111111111", CategoryConfiguration.CompetitiveId, seedDate),

                // Monopoly — Giải trí, Đối kháng
                Link("22222222-2222-2222-2222-222222222222", CategoryConfiguration.PartyId, seedDate),
                Link("22222222-2222-2222-2222-222222222222", CategoryConfiguration.CompetitiveId, seedDate),

                // Uno — Giải trí
                Link("33333333-3333-3333-3333-333333333333", CategoryConfiguration.PartyId, seedDate),

                // Splendor — Chiến thuật
                Link("44444444-4444-4444-4444-444444444444", CategoryConfiguration.StrategyId, seedDate),

                // Werewolf — Ẩn vai, Giải trí
                Link("55555555-5555-5555-5555-555555555555", CategoryConfiguration.HiddenRoleId, seedDate),
                Link("55555555-5555-5555-5555-555555555555", CategoryConfiguration.PartyId, seedDate),

                // Avalon — Ẩn vai, Chiến thuật
                Link("66666666-6666-6666-6666-666666666666", CategoryConfiguration.HiddenRoleId, seedDate),
                Link("66666666-6666-6666-6666-666666666666", CategoryConfiguration.StrategyId, seedDate),

                // Codenames — Ẩn vai, Giải trí
                Link("77777777-7777-7777-7777-777777777777", CategoryConfiguration.HiddenRoleId, seedDate),
                Link("77777777-7777-7777-7777-777777777777", CategoryConfiguration.PartyId, seedDate),

                // Pandemic — Hợp tác, Chiến thuật
                Link("88888888-8888-8888-8888-888888888888", CategoryConfiguration.CooperativeId, seedDate),
                Link("88888888-8888-8888-8888-888888888888", CategoryConfiguration.StrategyId, seedDate)
            );
        }

        private static GameTemplateCategory Link(string gameId, Guid categoryId, DateTime seedDate) =>
            new()
            {
                GameTemplateId = new Guid(gameId),
                CategoryId = categoryId,
                CreatedAt = seedDate
            };
    }
}
