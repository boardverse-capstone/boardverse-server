using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BoardVerse.Data.Configurations;

/// <summary>
/// EF Core configuration cho <see cref="User"/>.
/// Lưu ý: column <c>Status</c>-style enum trong DB lưu dạng <c>varchar</c>,
/// KHÔNG phải <c>int</c>. Phải dùng <c>HasConversion&lt;string&gt;()</c> cho
/// <see cref="User.Role"/> + <see cref="User.AccountStatus"/>.
/// Nếu thiếu, EF sẽ mặc định <c>HasConversion&lt;int&gt;()</c> cho enum → mismatch
/// với DB schema hiện tại → <c>InvalidCastException: Reading as 'System.Int32' is
/// not supported for fields having DataTypeName 'character varying'</c>.
/// </summary>
public class UserConfiguration : IEntityTypeConfiguration<User>
{
    public void Configure(EntityTypeBuilder<User> builder)
    {
        builder.ToTable("Users");
        builder.HasKey(u => u.Id);
        builder.Property(u => u.Id).ValueGeneratedNever();

        builder.Property(u => u.Username)
            .HasMaxLength(50)
            .IsRequired();
        builder.HasIndex(u => u.Username).IsUnique();

        builder.Property(u => u.Email)
            .HasMaxLength(255)
            .IsRequired();
        builder.HasIndex(u => u.Email).IsUnique();

        builder.Property(u => u.PhoneNumber).HasMaxLength(20);

        builder.Property(u => u.PasswordHash).HasMaxLength(255);
        builder.Property(u => u.PasswordResetToken).HasMaxLength(255);
        builder.Property(u => u.EmailVerificationToken).HasMaxLength(255);
        builder.Property(u => u.BlockReason).HasMaxLength(500);

        builder.Property(u => u.Provider).HasMaxLength(50).HasDefaultValue("Local");
        builder.Property(u => u.ProviderId).HasMaxLength(255);

        // ===== Enum → string conversion (match DB schema) =====
        builder.Property(u => u.Role)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(u => u.AccountStatus)
            .HasConversion<string>()
            .HasMaxLength(20)
            .IsRequired();

        builder.Property(u => u.IsActive).IsRequired().HasDefaultValue(true);
        builder.Property(u => u.IsEmailVerified).IsRequired().HasDefaultValue(false);
        builder.Property(u => u.CreatedAt).IsRequired();
        builder.Property(u => u.UpdatedAt).IsRequired();
        builder.Property(u => u.LastLoginAt);

        builder.HasIndex(u => u.ProviderId);
        builder.HasIndex(u => u.AccountStatus);
    }
}