namespace Ticketing.Application.Events;

/// <summary>Editable event fields shared by the create and update commands.</summary>
public interface IEventDetails
{
    string Name { get; }

    string Description { get; }

    string Venue { get; }

    DateTimeOffset StartsAt { get; }

    int Capacity { get; }
}
