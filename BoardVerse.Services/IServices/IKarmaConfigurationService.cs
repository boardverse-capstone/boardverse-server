namespace BoardVerse.Services.IServices
{
    public interface IKarmaConfigurationService
    {
        Task<int> GetLateCancelPenaltyAsync(CancellationToken cancellationToken = default);
        Task<int> GetNoShowPenaltyAsync(CancellationToken cancellationToken = default);
    }
}
