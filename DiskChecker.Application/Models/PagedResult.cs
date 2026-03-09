namespace DiskChecker.Application.Models;

/// <summary>
/// Generic paged result for pagination operations.
/// </summary>
/// <typeparam name="T">Type of items in the result.</typeparam>
public class PagedResult<T>
{
    /// <summary>
    /// Current page data.
    /// </summary>
    public IEnumerable<T> Items { get; set; } = Enumerable.Empty<T>();

    /// <summary>
    /// Current page number (1-based).
    /// </summary>
    public int PageNumber { get; set; }

    /// <summary>
    /// Number of items per page.
    /// </summary>
    public int PageSize { get; set; }

    /// <summary>
    /// Total number of items across all pages.
    /// </summary>
    public int TotalCount { get; set; }

    /// <summary>
    /// Total number of pages.
    /// </summary>
    public int TotalPages => (int)Math.Ceiling((double)TotalCount / PageSize);

    /// <summary>
    /// Whether there is a next page.
    /// </summary>
    public bool HasNextPage => PageNumber < TotalPages;

    /// <summary>
    /// Whether there is a previous page.
    /// </summary>
    public bool HasPreviousPage => PageNumber > 1;

    /// <summary>
    /// Legacy property name for backward compatibility.
    /// </summary>
    public int TotalItems
    {
        get => TotalCount;
        set => TotalCount = value;
    }

    /// <summary>
    /// Creates a new paged result.
    /// </summary>
    /// <param name="items">Items for current page.</param>
    /// <param name="pageNumber">Current page number.</param>
    /// <param name="pageSize">Number of items per page.</param>
    /// <param name="totalCount">Total number of items.</param>
#pragma warning disable CA1000
    public static PagedResult<T> Create(IEnumerable<T> items, int pageNumber, int pageSize, int totalCount)
    {
#pragma warning restore CA1000
        return new PagedResult<T>
        {
            Items = items,
            PageNumber = pageNumber,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }
}
