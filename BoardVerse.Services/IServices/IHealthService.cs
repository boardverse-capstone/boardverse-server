namespace BoardVerse.Services.IServices
{
    public interface IHealthService
    {
        Task<int> GetUserCountAsync(CancellationToken cancellationToken = default);
    }
}