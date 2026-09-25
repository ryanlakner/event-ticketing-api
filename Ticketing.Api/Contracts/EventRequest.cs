namespace Ticketing.Api.Contracts;

/// <summary>Payload for creating or updating an event.</summary>
/// <param name="Name">Display name.</param>
/// <param name="Description">Optional details shown to buyers.</param>
/// <param name="Venue">Where the event takes place.</param>
/// <param name="StartsAt">Start time; must be in the future. Stored in UTC.</param>
/// <param name="Capacity">Total seats available (1-100,000).</param>
public sealed record EventRequest(
    string Name,
    string? Description,
    string Venue,
    DateTimeOffset StartsAt,
    int Capacity
);
