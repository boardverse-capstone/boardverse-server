using BoardVerse.Core.DTOs.Friend;

namespace BoardVerse.Services.IServices;

public interface IFriendReportService
{
    Task<FriendReportDto> SubmitReportAsync(Guid reporterId, CreateFriendReportDto dto);
    Task<IReadOnlyList<FriendReportDto>> GetMyReportsAsync(Guid reporterId);
}
