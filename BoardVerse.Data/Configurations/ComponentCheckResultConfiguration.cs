using BoardVerse.Core.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations;

/// <summary>
/// BR-12: Audit trail cho component checklist.
/// Mỗi session game có 0..N dòng (mỗi component template 1 dòng) sau khi verify.
/// </summary>
public class ComponentCheckResultConfiguration : IEntityTypeConfiguration<ComponentCheckResult>
{
    public void Configure(EntityTypeBuilder<ComponentCheckResult> builder)
    {
        builder.ToTable("ComponentCheckResults");

        builder.HasKey(x => x.Id);

        builder.Property(x => x.ExpectedQuantity).IsRequired();
        builder.Property(x => x.ActualQuantity).IsRequired();
        builder.Property(x => x.PenaltyFee)
            .HasPrecision(18, 2)
            .IsRequired();
        builder.Property(x => x.ResponsibleMemberId)
            .IsRequired(false);
        builder.Property(x => x.StaffId).IsRequired();
        builder.Property(x => x.CheckedAt).IsRequired();

        // Idempotent: 1 (sessionGame, componentTemplate) chỉ có 1 dòng.
        builder.HasIndex(x => new { x.ActiveSessionGameId, x.GameComponentTemplateId })
            .IsUnique();

        // Audit query: lọc theo cafe qua session game → session → cafe.
        // Index phụ cho query theo staff.
        builder.HasIndex(x => x.ActiveSessionGameId);
        builder.HasIndex(x => x.StaffId);

        builder.HasOne(x => x.ActiveSessionGame)
            .WithMany(g => g.ComponentCheckResults)
            .HasForeignKey(x => x.ActiveSessionGameId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasOne(x => x.GameComponentTemplate)
            .WithMany()
            .HasForeignKey(x => x.GameComponentTemplateId)
            .OnDelete(DeleteBehavior.Restrict);

        builder.HasOne(x => x.Staff)
            .WithMany()
            .HasForeignKey(x => x.StaffId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}