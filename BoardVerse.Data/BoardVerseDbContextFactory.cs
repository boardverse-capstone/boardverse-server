using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using BoardVerse.Core.Entities;

namespace BoardVerse.Data;

public class BoardVerseDbContextFactory : IDesignTimeDbContextFactory<BoardVerseDbContext>
{
    public BoardVerseDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<BoardVerseDbContext>();

        // Try environment variable first (for CI/CD), then fallback to appsettings pattern
        var connectionString = Environment.GetEnvironmentVariable("DATABASE_URL")
            ?? Environment.GetEnvironmentVariable("ConnectionStrings__DefaultConnection")
            ?? "Host=localhost;Port=5432;Database=boardverse_dev;Username=postgres;Password=postgres;SSL Mode=Require";

        optionsBuilder.UseNpgsql(connectionString, npgsql =>
        {
            npgsql.UseNetTopologySuite();
        });

        return new BoardVerseDbContext(optionsBuilder.Options);
    }
}
