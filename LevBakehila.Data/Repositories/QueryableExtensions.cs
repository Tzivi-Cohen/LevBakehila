using LevBakehila.Core.DTOs.Common;
using Microsoft.EntityFrameworkCore;

namespace LevBakehila.Data.Repositories;

internal static class QueryableExtensions
{
    // Count + Skip/Take run in SQL Server - nothing is loaded into memory before paging
    public static async Task<PagedResult<T>> ToPagedResultAsync<T>(
        this IQueryable<T> query, int pageNumber, int pageSize, CancellationToken cancellationToken)
    {
        int totalCount = await query.CountAsync(cancellationToken);
        var items = await query
            .Skip((pageNumber - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        return new PagedResult<T>
        {
            Items = items,
            PageNumber = pageNumber,
            PageSize = pageSize,
            TotalCount = totalCount
        };
    }
}
