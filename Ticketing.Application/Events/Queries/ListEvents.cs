using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Abstractions.Data;
using Ticketing.Application.Abstractions.Identity;
using Ticketing.Application.Abstractions.Messaging;
using Ticketing.Application.Common.Models;
using Ticketing.Domain.Events;

namespace Ticketing.Application.Events.Queries;

/// <summary>Pages through events ordered by start time. Drafts appear only to their organizer.</summary>
public sealed record ListEventsQuery(
    int Page = 1,
    int PageSize = 20,
    string? Search = null,
    EventStatus? Status = null
) : IQuery<PagedResult<EventDto>>;

internal sealed class ListEventsQueryValidator : AbstractValidator<ListEventsQuery>
{
    public const int MaxPageSize = 100;

    public ListEventsQueryValidator()
    {
        RuleFor(q => q.Page).GreaterThanOrEqualTo(1);
        RuleFor(q => q.PageSize).InclusiveBetween(1, MaxPageSize);
        RuleFor(q => q.Search).MaximumLength(200);
        RuleFor(q => q.Status).IsInEnum();
    }
}

internal sealed class ListEventsQueryHandler(IApplicationDbContext db, ICurrentUser user)
    : IQueryHandler<ListEventsQuery, PagedResult<EventDto>>
{
    public async Task<PagedResult<EventDto>> HandleAsync(
        ListEventsQuery query,
        CancellationToken cancellationToken
    )
    {
        var events = db.Events.AsNoTracking().Where(EventAccess.VisibleTo(user.Id));

        if (query.Status is { } status)
        {
            events = events.Where(e => e.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(query.Search))
        {
            var term = query.Search.Trim();
            events = events.Where(e => e.Name.Contains(term) || e.Venue.Contains(term));
        }

        var totalCount = await events.CountAsync(cancellationToken);
        var items = await events
            .OrderBy(e => e.StartsAt)
            .ThenBy(e => e.Id)
            .Skip((query.Page - 1) * query.PageSize)
            .Take(query.PageSize)
            .Select(EventDto.Projection)
            .ToListAsync(cancellationToken);

        return new PagedResult<EventDto>(items, query.Page, query.PageSize, totalCount);
    }
}
