using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Abstractions.Data;
using Ticketing.Application.Abstractions.Identity;
using Ticketing.Application.Abstractions.Messaging;
using Ticketing.Application.Common.Exceptions;
using Ticketing.Domain.Reservations;

namespace Ticketing.Application.Reservations.Queries;

public sealed record GetReservationByIdQuery(Guid Id) : IQuery<ReservationDto>;

internal sealed class GetReservationByIdQueryHandler(IApplicationDbContext db, ICurrentUser user)
    : IQueryHandler<GetReservationByIdQuery, ReservationDto>
{
    // Visible only to the customer who holds it and the event's organizer; to anyone else it
    // does not exist.
    public async Task<ReservationDto> HandleAsync(
        GetReservationByIdQuery query,
        CancellationToken cancellationToken
    ) =>
        await db.ReservationsWithEvents()
            .Where(x =>
                x.Reservation.Id == query.Id
                && (x.Reservation.CustomerId == user.Id || x.Event.OrganizerId == user.Id)
            )
            .Select(ReservationDto.Projection)
            .SingleOrDefaultAsync(cancellationToken)
        ?? throw new NotFoundException(nameof(Reservation), query.Id);
}
