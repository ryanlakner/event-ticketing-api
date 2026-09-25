using Ticketing.Application.Abstractions.Data;
using Ticketing.Application.Abstractions.Identity;
using Ticketing.Application.Abstractions.Messaging;
using Ticketing.Application.Common.Exceptions;
using Ticketing.Domain.Events;

namespace Ticketing.Application.Events.Commands;

/// <summary>Opens a draft event for reservations.</summary>
public sealed record PublishEventCommand(Guid Id) : ICommand;

internal sealed class PublishEventCommandHandler(
    IApplicationDbContext db,
    TimeProvider clock,
    ICurrentUser user
) : ICommandHandler<PublishEventCommand, Unit>
{
    public async Task<Unit> HandleAsync(
        PublishEventCommand command,
        CancellationToken cancellationToken
    )
    {
        var @event =
            await db.Events.FindAsync([command.Id], cancellationToken)
            ?? throw new NotFoundException(nameof(Event), command.Id);
        EventAccess.EnsureCanManage(@event, user);

        @event.Publish(clock.GetUtcNow());
        await db.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
