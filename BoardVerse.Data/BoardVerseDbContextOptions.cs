using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Data
{
    public static class BoardVerseDbContextOptions
    {
        public static void UseBoardVersePostgreSql(DbContextOptionsBuilder options, string connectionString)
        {
            options.UseNpgsql(connectionString, npgsql => npgsql.UseNetTopologySuite());
        }
    }
}
