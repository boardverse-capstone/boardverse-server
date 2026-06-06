namespace BoardVerse.Core.Repositories
{
    public interface IHealthRepository
    {
        Task<int> CountUsersAsync();
    }
}
