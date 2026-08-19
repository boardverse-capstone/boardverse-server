using System.Text.Json;
using System.Text.Json.Serialization;
using BoardVerse.Core.Entities;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace BoardVerse.Data.Converters;

/// <summary>
/// EF Core value converter lưu <see cref="DepositSnapshot"/> vào cột <c>jsonb</c>
/// dùng <c>System.Text.Json</c> (BR-REQUIRED §17.5 audit).
/// </summary>
public class DepositSnapshotConverter : ValueConverter<DepositSnapshot, string>
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public DepositSnapshotConverter()
        : base(
            v => JsonSerializer.Serialize(v, Options),
            v => JsonSerializer.Deserialize<DepositSnapshot>(v, Options) ?? new DepositSnapshot())
    {
    }
}

/// <summary>
/// Phiên bản nullable cho <c>Lobby.DepositSnapshot</c>.
/// </summary>
public class NullableDepositSnapshotConverter : ValueConverter<DepositSnapshot?, string?>
{
    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    };

    public NullableDepositSnapshotConverter()
        : base(
            v => v == null ? null : JsonSerializer.Serialize(v, Options),
            v => v == null ? null : JsonSerializer.Deserialize<DepositSnapshot>(v, Options))
    {
    }
}
