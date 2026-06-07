using BoardVerse.Services.IServices;
using BoardVerse.Core.IRepositories;

namespace BoardVerse.Services.Services
{
    public class HealthService : IHealthService
    {
        private readonly IHealthRepository _userRepository;

        public HealthService(IHealthRepository userRepository)
        {
            _userRepository = userRepository;
        }

        public Task<int> GetUserCountAsync()
        {
            return _userRepository.CountUsersAsync();
        }
    }
}