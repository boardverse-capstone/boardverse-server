using BoardVerse.Core.Data;
using BoardVerse.Services.IServices;

namespace BoardVerse.Services.Services
{
    public class KarmaConfigurationService : IKarmaConfigurationService
    {
        private readonly ISystemConfigurationProvider _configurationProvider;

        public KarmaConfigurationService(ISystemConfigurationProvider configurationProvider)
        {
            _configurationProvider = configurationProvider;
        }

        public Task<int> GetLateCancelPenaltyAsync() =>
            _configurationProvider.GetIntAsync(SystemConfigKeys.KarmaPenaltyCancel, -3);

        public Task<int> GetNoShowPenaltyAsync() =>
            _configurationProvider.GetIntAsync(SystemConfigKeys.KarmaPenaltyNoshow, -5);
    }
}
