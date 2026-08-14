using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Services.Services;
using Xunit;

namespace BoardVerse.Tests.Services;

/// <summary>
/// Verify rằng khi lobby chuyển sang terminal status
/// (TimeoutFailed / HostCancelled / RejectedByCafe / ExpiredByCafe / Closed),
/// tất cả LobbyMembers phải được đánh dấu IsActive = false + Status = LobbyTerminated.
/// </summary>
public class LobbyMemberCleanupTests
{
    private static void InvokeMarkLobbyMembersInactive(Lobby lobby, DateTime now)
    {
        var method = typeof(ReservationService).GetMethod(
            "MarkLobbyMembersInactive",
            System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Static);

        Assert.NotNull(method);

        try
        {
            method.Invoke(null, new object[] { lobby, now });
        }
        catch (System.Reflection.TargetInvocationException ex)
        {
            // Unwrap inner exception for clearer test failure.
            throw ex.InnerException ?? ex;
        }
    }

    [Fact]
    public void MarkLobbyMembersInactive_FlipsAllActiveMembersToInactive()
    {
        var now = new DateTime(2026, 8, 14, 10, 0, 0, DateTimeKind.Utc);
        var lobby = new Lobby
        {
            Id = Guid.NewGuid(),
            Status = LobbyStatus.TimeoutFailed,
            Members = new List<LobbyMember>
            {
                new() { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), IsHost = true, IsActive = true, Status = LobbyMemberStatus.Joined, JoinedAt = now.AddHours(-2) },
                new() { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), IsHost = false, IsActive = true, Status = LobbyMemberStatus.Ready, JoinedAt = now.AddHours(-1) },
                new() { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), IsHost = false, IsActive = true, Status = LobbyMemberStatus.Joined, JoinedAt = now.AddMinutes(-30) }
            }
        };

        InvokeMarkLobbyMembersInactive(lobby, now);

        Assert.All(lobby.Members, m =>
        {
            Assert.False(m.IsActive);
            Assert.Equal(LobbyMemberStatus.LobbyTerminated, m.Status);
            Assert.Equal(now, m.LeftAt);
        });
    }

    [Fact]
    public void MarkLobbyMembersInactive_PreservesAlreadyInactiveMembers()
    {
        var now = new DateTime(2026, 8, 14, 10, 0, 0, DateTimeKind.Utc);
        var leftAt = now.AddMinutes(-15);
        var lobby = new Lobby
        {
            Id = Guid.NewGuid(),
            Status = LobbyStatus.TimeoutFailed,
            Members = new List<LobbyMember>
            {
                // Already left manually — should be untouched.
                new() { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), IsHost = false, IsActive = false, Status = LobbyMemberStatus.Left, LeftAt = leftAt },
                new() { Id = Guid.NewGuid(), UserId = Guid.NewGuid(), IsHost = true, IsActive = true, Status = LobbyMemberStatus.Joined, JoinedAt = now.AddHours(-1) }
            }
        };

        InvokeMarkLobbyMembersInactive(lobby, now);

        var alreadyLeft = lobby.Members.First(m => m.Status == LobbyMemberStatus.Left);
        Assert.False(alreadyLeft.IsActive);
        Assert.Equal(leftAt, alreadyLeft.LeftAt); // unchanged
        Assert.Equal(LobbyMemberStatus.Left, alreadyLeft.Status); // unchanged

        var stillActive = lobby.Members.First(m => m.IsHost);
        Assert.False(stillActive.IsActive);
        Assert.Equal(LobbyMemberStatus.LobbyTerminated, stillActive.Status);
        Assert.Equal(now, stillActive.LeftAt);
    }

    [Fact]
    public void MarkLobbyMembersInactive_HandlesEmptyMembersList()
    {
        var now = DateTime.UtcNow;
        var lobby = new Lobby
        {
            Id = Guid.NewGuid(),
            Status = LobbyStatus.TimeoutFailed,
            Members = new List<LobbyMember>()
        };

        // Should not throw.
        InvokeMarkLobbyMembersInactive(lobby, now);

        Assert.Empty(lobby.Members);
    }

    [Fact]
    public void MarkLobbyMembersInactive_HandlesNullMembersList()
    {
        var now = DateTime.UtcNow;
        var lobby = new Lobby
        {
            Id = Guid.NewGuid(),
            Status = LobbyStatus.TimeoutFailed,
            Members = null
        };

        // Should not throw.
        InvokeMarkLobbyMembersInactive(lobby, now);

        Assert.Null(lobby.Members);
    }
}
