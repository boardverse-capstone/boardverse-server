using Microsoft.EntityFrameworkCore;
using Npgsql;
using Xunit;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Unit test cho <see cref="BoardVerse.Services.Services.ReservationService"/>:
/// IsSerializationFailure detect Postgres SQLSTATE 40001 (serialization_failure)
/// hoặc 40P01 (deadlock_detected).
///
/// Test race condition thật sự (parallel confirm + SELECT FOR UPDATE + Serializable)
/// đã nằm trong <see cref="BoardVerse.Tests.Integration.ActiveSessionControllerConcurrencyTests"/> —
/// chạy trên nhánh testing (Postgres thật) để verify Serializable retry hoạt động.
/// </summary>
public class ReservationSerializationFailureDetectionTests
{
    [Fact]
    public void IsSerializationFailure_Should_ReturnTrue_WhenPostgresSqlStateIs40001()
    {
        // 40001 = serialization_failure — Postgres sẽ throw khi isolation = Serializable và conflict.
        var pgEx = CreateSerializationException("40001");
        var dbUpdate = new DbUpdateException("DB update failed", pgEx);

        var result = InvokeIsSerializationFailure(dbUpdate);

        Assert.True(result);
    }

    [Fact]
    public void IsSerializationFailure_Should_ReturnTrue_WhenPostgresSqlStateIs40P01()
    {
        // 40P01 = deadlock_detected.
        var pgEx = CreateSerializationException("40P01");
        var dbUpdate = new DbUpdateException("DB update failed", pgEx);

        var result = InvokeIsSerializationFailure(dbUpdate);

        Assert.True(result);
    }

    [Fact]
    public void IsSerializationFailure_Should_ReturnFalse_WhenGenericDbException()
    {
        // Không có inner PostgresException → false.
        var dbUpdate = new DbUpdateException("Generic DB error");

        var result = InvokeIsSerializationFailure(dbUpdate);

        Assert.False(result);
    }

    [Fact]
    public void IsSerializationFailure_Should_ReturnFalse_WhenPostgresNonSerializationError()
    {
        // 23505 = unique_violation, không phải serialization/deadlock.
        var pgEx = CreateSerializationException("23505");
        var dbUpdate = new DbUpdateException("DB update failed", pgEx);

        var result = InvokeIsSerializationFailure(dbUpdate);

        Assert.False(result);
    }

    /// <summary>
    /// Helper: tạo PostgresException với SqlState nhất định.
    /// Dùng Activator vì constructor PostgresException không public mọi overload.
    /// </summary>
    private static PostgresException CreateSerializationException(string sqlState)
    {
        // PostgresException constructor đầy đủ: (messageText, severity, invariantSeverity, sqlState, [FileName], [Line], [Routine])
        var ctors = typeof(PostgresException).GetConstructors(
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);

        // Tìm constructor phù hợp nhất.
        // Thường là: (string messageText, string severity, string invariantSeverity, string sqlState)
        var ctor = ctors.FirstOrDefault(c =>
        {
            var p = c.GetParameters();
            return p.Length == 4
                && p[0].ParameterType == typeof(string)
                && p[1].ParameterType == typeof(string)
                && p[2].ParameterType == typeof(string)
                && p[3].ParameterType == typeof(string);
        });

        if (ctor == null)
        {
            // Fallback: dùng constructor phổ biến nhất.
            ctor = ctors.First();
        }

        return (PostgresException)ctor.Invoke(new object[]
        {
            "test exception",
            "ERROR",
            "ERROR",
            sqlState
        });
    }

    /// <summary>Reflection helper: gọi private static method IsSerializationFailure.</summary>
    private static bool InvokeIsSerializationFailure(DbUpdateException ex)
    {
        var assembly = typeof(BoardVerse.Services.Services.ReservationService).Assembly;
        var serviceType = assembly.GetType("BoardVerse.Services.Services.ReservationService")
            ?? throw new InvalidOperationException("ReservationService type not found.");
        var method = serviceType.GetMethod(
            "IsSerializationFailure",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static)
            ?? throw new InvalidOperationException("IsSerializationFailure method not found.");

        return (bool)method.Invoke(null, new object[] { ex })!;
    }
}