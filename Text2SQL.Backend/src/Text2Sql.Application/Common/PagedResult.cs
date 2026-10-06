namespace Text2Sql.Application.Common
{
    /// <summary>
    /// Sayfalı sonuç. v1'de yalnızca <see cref="Items"/> döner (geriye uyumluluk),
    /// v2'de sayfalama bilgisi X-Total-Count / X-Page / X-Page-Size başlıklarıyla
    /// taşınır (GitHub API deseni — gövdeyi kirletmez, istemci akışını basitleştirir).
    /// </summary>
    public sealed class PagedResult<T>
    {
        public IReadOnlyList<T> Items { get; init; } = Array.Empty<T>();
        public int Page { get; init; } = 1;
        public int PageSize { get; init; }
        public int TotalCount { get; init; }

        public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling((double)TotalCount / PageSize);

        public static PagedResult<T> Create(IReadOnlyList<T> items, int page, int pageSize, int total)
            => new() { Items = items, Page = page, PageSize = pageSize, TotalCount = total };
    }
}
