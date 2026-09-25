using FluentValidation;
using Ticketing.Application.Abstractions.Data;
using Ticketing.Application.Abstractions.Messaging;
using Ticketing.Application.Common.Exceptions;
using Ticketing.Domain.Events;

namespace Ticketing.Application.Events.Commands;

public sealed record UpdateEventCommand(
    Guid Id,
    string Name,
    string Description,
    string Venue,
    DateTimeOffset StartsAt,
    int Capacity
) : ICommand, IEventDetails;

internal sealed class UpdateEventCommandValidator : AbstractValidator<UpdateEventCommand>
{
    public UpdateEventCommandValidator(TimeProvider clock)
    {
        RuleFor(c => c.Id).NotEmpty();
        Include(new EventDetailsValidator(clock));
    }
}

internal sealed class UpdateEventCommandHandler(IApplicationDbContext db, TimeProvider clock)
    : ICommandHandler<UpdateEventCommand, Unit>
{
    public async Task<Unit> HandleAsync(
        UpdateEventCommand command,
        CancellationToken cancellationToken
    )
    {
        var @event =
            await db.Events.FindAsync([command.Id], cancellationToken)
            ?? throw new NotFoundException(nameof(Event), command.Id);

        @event.UpdateDetails(
            command.Name,
            command.Description,
            command.Venue,
            command.StartsAt,
            command.Capacity,
            clock.GetUtcNow()
        );
        await db.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
