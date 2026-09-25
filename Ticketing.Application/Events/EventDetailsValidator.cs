using FluentValidation;
using Ticketing.Domain.Events;

namespace Ticketing.Application.Events;

internal sealed class EventDetailsValidator : AbstractValidator<IEventDetails>
{
    public EventDetailsValidator(TimeProvider clock)
    {
        RuleFor(e => e.Name).NotEmpty().MaximumLength(Event.NameMaxLength);
        RuleFor(e => e.Description).NotNull().MaximumLength(Event.DescriptionMaxLength);
        RuleFor(e => e.Venue).NotEmpty().MaximumLength(Event.VenueMaxLength);
        RuleFor(e => e.StartsAt)
            .Must(startsAt => startsAt > clock.GetUtcNow())
            .WithMessage("'Starts At' must be in the future.");
        RuleFor(e => e.Capacity).InclusiveBetween(1, Event.MaxCapacity);
    }
}
