using BoardVerse.Core.DTOs.Game;
using BoardVerse.Core.Entities;
using BoardVerse.Core.Enum;
using BoardVerse.Core.Exceptions;
using BoardVerse.Core.IRepositories;
using BoardVerse.Services.Services;
using Moq;

namespace BoardVerse.Tests.Services;

public class BoardGameServiceTests
{
    private static readonly Guid GameId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public async Task GetPlayConfigurationAsync_SoloGame_ExposesSoloAndGroupModes()
    {
        var gameRepo = new Mock<IGameTemplateRepository>();
        gameRepo.Setup(r => r.GetActiveByIdWithComponentsAsync(GameId))
            .ReturnsAsync(new GameTemplate
            {
                Id = GameId,
                Name = "Solo Game",
                MinPlayers = 1,
                MaxPlayers = 4,
                PlayTime = 30
            });

        var service = new BoardGameService(gameRepo.Object, Mock.Of<ICategoryRepository>());
        var config = await service.GetPlayConfigurationAsync(GameId);

        Assert.True(config.SupportsSoloPlay);
        Assert.Contains(PlayerPlayMode.Solo, config.AvailablePlayModes);
        Assert.Contains(PlayerPlayMode.Group, config.AvailablePlayModes);
    }

    [Fact]
    public async Task ResolvePlayNavigationAsync_SoloOnMultiplayerGame_ThrowsBadRequest()
    {
        var gameRepo = new Mock<IGameTemplateRepository>();
        gameRepo.Setup(r => r.GetActiveByIdWithComponentsAsync(GameId))
            .ReturnsAsync(new GameTemplate
            {
                Id = GameId,
                Name = "Catan",
                MinPlayers = 3,
                MaxPlayers = 4,
                PlayTime = 60
            });

        var service = new BoardGameService(gameRepo.Object, Mock.Of<ICategoryRepository>());

        await Assert.ThrowsAsync<BadRequestException>(() =>
            service.ResolvePlayNavigationAsync(GameId, new ResolveGamePlayNavigationRequestDto
            {
                PlayMode = PlayerPlayMode.Solo
            }));
    }

    [Fact]
    public async Task ResolvePlayNavigationAsync_GroupMode_ReturnsLobbyCreation()
    {
        var gameRepo = new Mock<IGameTemplateRepository>();
        gameRepo.Setup(r => r.GetActiveByIdWithComponentsAsync(GameId))
            .ReturnsAsync(new GameTemplate
            {
                Id = GameId,
                Name = "Catan",
                MinPlayers = 3,
                MaxPlayers = 4,
                PlayTime = 60
            });

        var service = new BoardGameService(gameRepo.Object, Mock.Of<ICategoryRepository>());
        var result = await service.ResolvePlayNavigationAsync(GameId, new ResolveGamePlayNavigationRequestDto
        {
            PlayMode = PlayerPlayMode.Group
        });

        Assert.Equal(GamePlayNavigationTarget.LobbyCreation, result.NavigationTarget);
        Assert.NotNull(result.RoomConfiguration);
        Assert.Equal(3, result.RoomConfiguration.MinPlayers);
        Assert.Equal(4, result.RoomConfiguration.MaxPlayers);
        Assert.Equal(3, result.RoomConfiguration.DefaultPlayerCount);
    }
}
