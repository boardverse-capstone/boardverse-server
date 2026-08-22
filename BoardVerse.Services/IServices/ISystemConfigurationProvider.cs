namespace BoardVerse.Services.IServices
{
    public interface ISystemConfigurationProvider
    {
        Task<int> GetIntAsync(string key, int fallback, CancellationToken cancellationToken = default);
        Task<double> GetDoubleAsync(string key, double fallback, CancellationToken cancellationToken = default);
        Task<bool> GetBoolAsync(string key, bool fallback, CancellationToken cancellationToken = default);
        Task<string> GetStringAsync(string key, string fallback, CancellationToken cancellationToken = default);
        Task InvalidateCacheAsync(CancellationToken cancellationToken = default);
    }
}
