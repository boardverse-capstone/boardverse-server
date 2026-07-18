using BoardVerse.Core.DTOs.Payment;
using BoardVerse.Core.Entities;

namespace BoardVerse.Services.IServices
{
    public interface ISePayAccountService
    {
        Task<SePayAccountDto?> GetByIdAsync(Guid id);
        Task<SePayAccountDto?> GetByCafeIdAsync(Guid cafeId);
        Task<SePayAccountDto?> GetMasterAccountAsync();
        Task<IReadOnlyList<SePayAccountDto>> GetAllAsync(SePayAccountQuery? query = null);
        Task<SePayAccountDto> CreateAsync(CreateSePayAccountRequestDto request);
        Task<SePayAccountDto> UpdateAsync(Guid id, UpdateSePayAccountRequestDto request);
        Task<SePayAccountDto> SetEnvironmentAsync(Guid id, string environment);
        Task DeleteAsync(Guid id);

        // Manager endpoints - operates on cafe that manager owns
        Task<SePayAccountDto?> GetByManagerCafeAsync();
        Task<SePayAccountDto> UpdateByManagerCafeAsync(UpdateSePayAccountRequestDto request);
        Task<SePayAccountDto> SetEnvironmentByManagerCafeAsync(string environment);
    }
}
