namespace BoardVerse.Services.IServices
{
    public interface ISystemConfigurationProvider
    {
        Task<int> GetIntAsync(string key, int fallback);
        Task<double> GetDoubleAsync(string key, double fallback);
        Task<string> GetStringAsync(string key, string fallback);
        Task InvalidateCacheAsync();
    }
}
