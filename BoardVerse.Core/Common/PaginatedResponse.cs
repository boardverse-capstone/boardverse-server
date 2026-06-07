namespace BoardVerse.Core.Common;

public class PaginatedResponse
{
    public IEnumerable<object> Data { get; set; } = Enumerable.Empty<object>();
    public PaginationMeta Meta { get; set; } = new PaginationMeta();
}

public class PaginatedResponse<T> : PaginatedResponse
{
    public new IEnumerable<T> Data { get; set; } = Enumerable.Empty<T>();
}
