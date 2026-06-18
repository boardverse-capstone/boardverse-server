using System.Net;
using BoardVerse.Core.Data;
using BoardVerse.Core.Enum;

namespace BoardVerse.Tests.Integration.Infrastructure;

public static class IntegrationCatalog
{
    public static async Task<Guid> GetCatanGameIdAsync(HttpClient client)
    {
        var response = await client.GetAsync("/api/v1/board-games?search=catan&pageSize=1");
        response.EnsureSuccessStatusCode();
        var body = await ApiTestClient.ReadApiResponseAsync<PaginatedBoardGamesDto>(response);
        return body.Data!.Data.FirstOrDefault()?.Id
            ?? throw new InvalidOperationException("No Catan game in catalog — run game seed first.");
    }

    public static async Task<DemoInventorySnapshot> GetDemoCafeInventoryAsync(HttpClient client, Guid gameId)
    {
        var response = await client.GetAsync(
            $"/api/cafes/{DevSeedConstants.DemoCafeId}/inventory?pageSize=50");
        response.EnsureSuccessStatusCode();
        var body = await ApiTestClient.ReadApiResponseAsync<PaginatedInventoryDto>(response);
        var row = body.Data!.Data.FirstOrDefault(i => i.GameTemplateId == gameId)
            ?? throw new InvalidOperationException("Demo cafe has no inventory for Catan.");

        return new DemoInventorySnapshot(row.Id, row.GameTemplateId, row.Boxes);
    }

    public static async Task<(Guid TableId, string Barcode)> FindAvailablePosTargetsAsync(
        HttpClient client,
        Guid gameId)
    {
        var tablesResponse = await client.GetAsync(
            $"/api/cafes/{DevSeedConstants.DemoCafeId}/pos/tables");
        tablesResponse.EnsureSuccessStatusCode();
        var tables = (await ApiTestClient.ReadApiResponseAsync<List<PosTableDto>>(tablesResponse)).Data!
            .FirstOrDefault(t => t.Status == CafeTableStatus.Available);

        var boxesResponse = await client.GetAsync(
            $"/api/cafes/{DevSeedConstants.DemoCafeId}/pos/boxes?gameTemplateId={gameId}");
        boxesResponse.EnsureSuccessStatusCode();
        var box = (await ApiTestClient.ReadApiResponseAsync<List<PosBoxDto>>(boxesResponse)).Data!
            .FirstOrDefault(b => b.Status == CafeGameInventoryStatus.Available);

        if (tables == null || box == null)
        {
            throw new InvalidOperationException("No available table/box for POS session test.");
        }

        return (tables.Id, box.Barcode);
    }

    public sealed record DemoInventorySnapshot(
        Guid InventoryId,
        Guid GameTemplateId,
        List<PosBoxDto> Boxes);

    public static async Task<Guid> GetMasterGameIdAsync(HttpClient client, string searchTerm)
    {
        var response = await client.GetAsync(
            $"/api/v1/master-games?searchTerm={Uri.EscapeDataString(searchTerm)}&cafeId={DevSeedConstants.DemoCafeId}&pageSize=1");
        response.EnsureSuccessStatusCode();
        var games = (await ApiTestClient.ReadApiResponseAsync<MasterGameListDto>(response)).Data?.Data ?? [];
        if (games.Count == 0)
        {
            throw new InvalidOperationException($"No master game found for search '{searchTerm}'.");
        }

        return games[0].Id;
    }

    public static async Task<Guid> GetOrCreateCafeInventoryIdAsync(
        HttpClient client,
        Guid cafeId,
        Guid gameTemplateId,
        int boxQuantity = 1)
    {
        var addResponse = await ApiTestClient.PostJsonAsync(client, $"/api/cafes/{cafeId}/inventory", new
        {
            gameTemplateId,
            boxQuantity,
            status = CafeGameInventoryStatus.Available
        });

        if (addResponse.StatusCode == HttpStatusCode.Created)
        {
            return (await ApiTestClient.ReadApiResponseAsync<InventoryCreatedDto>(addResponse)).Data!.Id;
        }

        if (addResponse.StatusCode != HttpStatusCode.Conflict)
        {
            addResponse.EnsureSuccessStatusCode();
        }

        var listResponse = await client.GetAsync($"/api/cafes/{cafeId}/inventory?pageSize=50");
        listResponse.EnsureSuccessStatusCode();
        var row = (await ApiTestClient.ReadApiResponseAsync<PaginatedInventoryDto>(listResponse)).Data!.Data
            .FirstOrDefault(i => i.GameTemplateId == gameTemplateId)
            ?? throw new InvalidOperationException($"Inventory for game {gameTemplateId} not found after conflict.");

        return row.Id;
    }

    public sealed class MasterGameListDto
    {
        public List<MasterGameSummaryDto> Data { get; set; } = [];
    }

    public sealed class MasterGameSummaryDto
    {
        public Guid Id { get; set; }
    }

    public sealed class InventoryCreatedDto
    {
        public Guid Id { get; set; }
    }

    public sealed class PaginatedBoardGamesDto
    {
        public List<BoardGameSummaryDto> Data { get; set; } = [];
    }

    public sealed class BoardGameSummaryDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
    }

    public sealed class PaginatedInventoryDto
    {
        public List<InventoryRowDto> Data { get; set; } = [];
    }

    public sealed class InventoryRowDto
    {
        public Guid Id { get; set; }
        public Guid GameTemplateId { get; set; }
        public List<PosBoxDto> Boxes { get; set; } = [];
    }

    public sealed class PosTableDto
    {
        public Guid Id { get; set; }
        public string Name { get; set; } = string.Empty;
        public CafeTableStatus Status { get; set; }
    }

    public sealed class PosBoxDto
    {
        public Guid Id { get; set; }
        public string Barcode { get; set; } = string.Empty;
        public CafeGameInventoryStatus Status { get; set; }
    }
}
