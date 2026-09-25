using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Abstractions.Data;
using Ticketing.Application.Abstractions.Messaging;
using Ticketing.Application.Common.Exceptions;
using Ticketing.Domain.Events;

namespace Ticketing.Application.Events.Queries;

public sealed record GetEventByIdQuery(Guid Id) : IQuery<EventDto>;

internal sealed class GetEventByIdQueryHandler(IApplicationDbContext db)
    : IQueryHandler<GetEventByIdQuery, EventDto>
{
    public async Task<EventDto> HandleAsync(
        GetEventByIdQuery query,
        CancellationToken cancellationToken
    ) =>
        await db
            .Events.AsNoTracking()
            .Where(e => e.Id == query.Id)
            .Select(EventDto.Projection)
            .SingleOrDefaultAsync(cancellationToken)
        ?? throw new NotFoundException(nameof(Event), query.Id);
}
