namespace BoardVerse.Core.Common;

public abstract class PaginatedResponseBase
{
    public PaginationMeta Meta { get; set; } = new PaginationMeta();
}

public class PaginatedResponse<T> : PaginatedResponseBase
{
    public IEnumerable<T> Data { get; set; } = Enumerable.Empty<T>();
}
