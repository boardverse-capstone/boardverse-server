namespace BoardVerse.Services.IServices;

public interface ICurrentUserService
{
    Guid? GetCurrentUserId();
}
