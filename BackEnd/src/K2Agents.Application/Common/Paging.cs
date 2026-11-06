using Microsoft.EntityFrameworkCore;

namespace K2Agents.Application.Common;

public record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);

public abstract record PagedQuery
{
    public int Page { get; init; } = 1;
    public int PageSize { get; init; } = 20;

    public int SafePage => Math.Max(1, Page);
    public int SafePageSize => Math.Clamp(PageSize, 1, 100);
}

public static class PagingExtensions
{
    public static async Task<PagedResult<T>> ToPagedAsync<T>(this IQueryable<T> query, PagedQuery paging, CancellationToken ct)
    {
        var total = await query.CountAsync(ct);
        var items = await query.Skip((paging.SafePage - 1) * paging.SafePageSize).Take(paging.SafePageSize).ToListAsync(ct);
        return new PagedResult<T>(items, total, paging.SafePage, paging.SafePageSize);
    }
}
