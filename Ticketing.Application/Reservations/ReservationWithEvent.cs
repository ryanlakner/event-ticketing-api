using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Abstractions.Data;
using Ticketing.Domain.Events;
using Ticketing.Domain.Reservations;

namespace Ticketing.Application.Reservations;

/// <summary>
/// A reservation joined to its event, so read queries can filter and sort on either side before
/// projecting to <see cref="ReservationDto"/>. Uses init properties rather than a constructor
/// because EF Core can only compose further LINQ over member-initialized types.
/// </summary>
internal sealed class ReservationWithEvent
{
    public required Reservation Reservation { get; init; }

    public required Event Event { get; init; }
}

internal static class ReservationReadModel
{
    public static IQueryable<ReservationWithEvent> ReservationsWithEvents(
        this IApplicationDbContext db
    ) =>
        from r in db.Reservations.AsNoTracking()
        join e in db.Events.AsNoTracking() on r.EventId equals e.Id
        select new ReservationWithEvent { Reservation = r, Event = e };
}
