namespace BoardVerse.Services.IServices
{
    public interface IKarmaConfigurationService
    {
        Task<int> GetLateCancelPenaltyAsync();
        Task<int> GetNoShowPenaltyAsync();
    }
}
