namespace ProductionLinePlanner.Application.Common;

public class PagedResult<T> : Result<T[]>
{
    private PagedResult(T[] items, int pageNumber, int pageSize, int totalCount, bool isSuccess, Error? error)
        : base(items, isSuccess, error)
    {
        PageNumber = pageNumber;
        PageSize = pageSize;
        TotalCount = totalCount;
        TotalPages = pageSize > 0 ? (int)Math.Ceiling(totalCount / (double)pageSize) : 0;
    }

    public int PageNumber { get; }

    public int PageSize { get; }

    public int TotalCount { get; }

    public int TotalPages { get; }

    public IReadOnlyList<T> Items => Value ?? [];

    public static PagedResult<T> Success(
        IEnumerable<T> items,
        int pageNumber,
        int pageSize,
        int totalCount)
    {
        return new PagedResult<T>(items.ToArray(), pageNumber, pageSize, totalCount, true, null);
    }

    public static new PagedResult<T> Failure(Error error)
    {
        return new PagedResult<T>([], 1, 0, 0, false, error ?? throw new ArgumentNullException(nameof(error)));
    }
}
