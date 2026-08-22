namespace BoardVerse.Core.IRepositories
{
    public interface IHealthRepository
    {
        Task<int> CountUsersAsync(CancellationToken cancellationToken = default);
    }
}
