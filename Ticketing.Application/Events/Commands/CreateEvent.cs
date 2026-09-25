using FluentValidation;
using Ticketing.Application.Abstractions.Data;
using Ticketing.Application.Abstractions.Identity;
using Ticketing.Application.Abstractions.Messaging;
using Ticketing.Application.Common.Security;
using Ticketing.Domain.Events;

namespace Ticketing.Application.Events.Commands;

/// <summary>Creates an event in <see cref="EventStatus.Draft"/>; it must be published before tickets can be reserved.</summary>
public sealed record CreateEventCommand(
    string Name,
    string Description,
    string Venue,
    DateTimeOffset StartsAt,
    int Capacity
) : ICommand<Guid>, IEventDetails;

internal sealed class CreateEventCommandValidator : AbstractValidator<CreateEventCommand>
{
    public CreateEventCommandValidator(TimeProvider clock) =>
        Include(new EventDetailsValidator(clock));
}

internal sealed class CreateEventCommandHandler(
    IApplicationDbContext db,
    TimeProvider clock,
    ICurrentUser user
) : ICommandHandler<CreateEventCommand, Guid>
{
    public async Task<Guid> HandleAsync(
        CreateEventCommand command,
        CancellationToken cancellationToken
    )
    {
        var @event = Event.Create(
            user.RequireId(),
            command.Name,
            command.Description,
            command.Venue,
            command.StartsAt,
            command.Capacity,
            clock.GetUtcNow()
        );

        db.Events.Add(@event);
        await db.SaveChangesAsync(cancellationToken);

        return @event.Id;
    }
}
