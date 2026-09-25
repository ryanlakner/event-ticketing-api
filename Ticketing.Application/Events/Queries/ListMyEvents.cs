using FluentValidation;
using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Abstractions.Data;
using Ticketing.Application.Abstractions.Identity;
using Ticketing.Application.Abstractions.Messaging;
using Ticketing.Application.Common.Models;
using Ticketing.Application.Common.Security;
using Ticketing.Domain.Events;

namespace Ticketing.Application.Events.Queries;

/// <summary>Events the caller organizes, drafts included, ordered by start time.</summary>
public sealed record ListMyEventsQuery(int Page = 1, int PageSize = 20, EventStatus? Status = null)
    : IQuery<PagedResult<EventDto>>,
        IPagedQuery;

internal sealed class ListMyEventsQueryValidator : AbstractValidator<ListMyEventsQuery>
{
    public ListMyEventsQueryValidator()
    {
        Include(new PagedQueryValidator());
        RuleFor(q => q.Status).IsInEnum();
    }
}

internal sealed class ListMyEventsQueryHandler(IApplicationDbContext db, ICurrentUser user)
    : IQueryHandler<ListMyEventsQuery, PagedResult<EventDto>>
{
    public Task<PagedResult<EventDto>> HandleAsync(
        ListMyEventsQuery query,
        CancellationToken cancellationToken
    )
    {
        var organizerId = user.RequireId();
        var events = db.Events.AsNoTracking().Where(e => e.OrganizerId == organizerId);

        if (query.Status is { } status)
        {
            events = events.Where(e => e.Status == status);
        }

        return events
            .OrderBy(e => e.StartsAt)
            .ThenBy(e => e.Id)
            .ToPagedResultAsync(EventDto.Projection, query, cancellationToken);
    }
}
