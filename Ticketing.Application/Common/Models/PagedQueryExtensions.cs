using System.Linq.Expressions;
using Microsoft.EntityFrameworkCore;

namespace Ticketing.Application.Common.Models;

internal static class PagedQueryExtensions
{
    /// <summary>
    /// Counts the full result, then fetches and projects just the requested page. Requiring an
    /// ordered source makes paging deterministic; projecting last keeps it translatable to SQL.
    /// </summary>
    public static async Task<PagedResult<TResult>> ToPagedResultAsync<TSource, TResult>(
        this IOrderedQueryable<TSource> ordered,
        Expression<Func<TSource, TResult>> projection,
        IPagedQuery query,
        CancellationToken cancellationToken
    )
    {
        var totalCount = await ordered.CountAsync(cancellationToken);
        var items = await ordered
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(projection)
            .ToListAsync(cancellationToken);

        return new PagedResult<TResult>(items, query.Page, query.PageSize, totalCount);
    }
}
