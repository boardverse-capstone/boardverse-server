using BoardVerse.Core.Repositories;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data.Repositories
{
    public class HealthRepository : IHealthRepository
    {
        private readonly BoardVerseDbContext _context;

        public HealthRepository(BoardVerseDbContext context)
        {
            _context = context;
        }

        public Task<int> CountUsersAsync()
        {
            return _context.Users.CountAsync();
        }
    }
}
