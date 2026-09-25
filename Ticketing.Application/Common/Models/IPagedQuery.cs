namespace Ticketing.Application.Common.Models;

/// <summary>A query that returns one page of results.</summary>
public interface IPagedQuery
{
    /// <summary>1-based page number.</summary>
    int Page { get; }

    int PageSize { get; }
}
