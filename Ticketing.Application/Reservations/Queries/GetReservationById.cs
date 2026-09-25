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
    // Read model joins in event details so clients don't need a second round trip. Visible only
    // to the customer who holds it and the event's organizer; to anyone else it does not exist.
    public async Task<ReservationDto> HandleAsync(
        GetReservationByIdQuery query,
        CancellationToken cancellationToken
    ) =>
        await (
            from r in db.Reservations.AsNoTracking()
            join e in db.Events.AsNoTracking() on r.EventId equals e.Id
            where r.Id == query.Id && (r.CustomerId == user.Id || e.OrganizerId == user.Id)
            select new ReservationDto(
                r.Id,
                r.EventId,
                e.Name,
                e.StartsAt,
                r.CustomerId,
                r.CustomerEmail,
                r.Quantity,
                r.Status,
                r.ExpiresAt,
                r.ConfirmedAt,
                r.CreatedAt
            )
        ).SingleOrDefaultAsync(cancellationToken)
        ?? throw new NotFoundException(nameof(Reservation), query.Id);
}
