using System.Linq.Expressions;
using Ticketing.Domain.Events;

namespace Ticketing.Application.Events;

public sealed record EventDto(
    Guid Id,
    string Name,
    string Description,
    string Venue,
    DateTimeOffset StartsAt,
    int Capacity,
    int SeatsAvailable,
    EventStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? UpdatedAt
)
{
    /// <summary>Projection translated to SQL so queries only select the columns they need.</summary>
    internal static readonly Expression<Func<Event, EventDto>> Projection = e => new EventDto(
        e.Id,
        e.Name,
        e.Description,
        e.Venue,
        e.StartsAt,
        e.Capacity,
        e.Capacity - e.SeatsReserved,
        e.Status,
        e.CreatedAt,
        e.UpdatedAt
    );
}
