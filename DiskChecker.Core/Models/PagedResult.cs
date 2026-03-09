namespace DiskChecker.Core.Models {
      public class PagedResult<T> {
          public IReadOnlyList<T> Items { get; set; } = Array.Empty<T>();
          public int TotalItems { get; set; }
          public int PageSize { get; set; }
          public int PageIndex { get; set; }
          public int TotalPages => (int)Math.Ceiling((double)TotalItems / PageSize);
      }
    }