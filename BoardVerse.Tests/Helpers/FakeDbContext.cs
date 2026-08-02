using BoardVerse.Data;
using Microsoft.EntityFrameworkCore;

namespace BoardVerse.Tests.Helpers;

/// <summary>
/// Stub DbContext dùng cho unit test không cần provider thật.
/// Chỉ override constructor để nhận options rỗng mà không gọi base OnModelCreating.
/// </summary>
public class FakeDbContext : BoardVerseDbContext
{
    public FakeDbContext() : base(new DbContextOptionsBuilder<BoardVerseDbContext>().Options)
    {
    }
}