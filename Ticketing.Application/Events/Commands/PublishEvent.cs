using Ticketing.Application.Abstractions.Data;
using Ticketing.Application.Abstractions.Messaging;
using Ticketing.Application.Common.Exceptions;
using Ticketing.Domain.Events;

namespace Ticketing.Application.Events.Commands;

/// <summary>Opens a draft event for reservations.</summary>
public sealed record PublishEventCommand(Guid Id) : ICommand;

internal sealed class PublishEventCommandHandler(IApplicationDbContext db, TimeProvider clock)
    : ICommandHandler<PublishEventCommand, Unit>
{
    public async Task<Unit> HandleAsync(
        PublishEventCommand command,
        CancellationToken cancellationToken
    )
    {
        var @event =
            await db.Events.FindAsync([command.Id], cancellationToken)
            ?? throw new NotFoundException(nameof(Event), command.Id);

        @event.Publish(clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
