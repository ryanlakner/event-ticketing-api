using FluentValidation;
using Ticketing.Application.Abstractions.Data;
using Ticketing.Application.Abstractions.Identity;
using Ticketing.Application.Abstractions.Messaging;
using Ticketing.Application.Common.Models;
using Ticketing.Application.Common.Security;
using Ticketing.Domain.Reservations;

namespace Ticketing.Application.Reservations.Queries;

/// <summary>The caller's own reservations, soonest event first.</summary>
public sealed record ListMyReservationsQuery(
    int Page = 1,
    int PageSize = 20,
    ReservationStatus? Status = null
) : IQuery<PagedResult<ReservationDto>>, IPagedQuery;

internal sealed class ListMyReservationsQueryValidator : AbstractValidator<ListMyReservationsQuery>
{
    public ListMyReservationsQueryValidator()
    {
        Include(new PagedQueryValidator());
        RuleFor(q => q.Status).IsInEnum();
    }
}

internal sealed class ListMyReservationsQueryHandler(IApplicationDbContext db, ICurrentUser user)
    : IQueryHandler<ListMyReservationsQuery, PagedResult<ReservationDto>>
{
    public Task<PagedResult<ReservationDto>> HandleAsync(
        ListMyReservationsQuery query,
        CancellationToken cancellationToken
    )
    {
        var customerId = user.RequireId();
        var reservations = db.ReservationsWithEvents()
            .Where(x => x.Reservation.CustomerId == customerId);

        if (query.Status is { } status)
        {
            reservations = reservations.Where(x => x.Reservation.Status == status);
        }

        return reservations
            .OrderBy(x => x.Event.StartsAt)
            .ThenByDescending(x => x.Reservation.CreatedAt)
            .ThenBy(x => x.Reservation.Id)
            .ToPagedResultAsync(ReservationDto.Projection, query, cancellationToken);
    }
}
