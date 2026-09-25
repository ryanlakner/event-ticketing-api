using System.Linq.Expressions;
using Ticketing.Application.Abstractions.Identity;
using Ticketing.Application.Common.Exceptions;
using Ticketing.Domain.Events;

namespace Ticketing.Application.Events;

/// <summary>Who can see and manage an event. Kept in one place so every handler agrees.</summary>
internal static class EventAccess
{
    /// <summary>Drafts are invisible to everyone except their organizer.</summary>
    public static Expression<Func<Event, bool>> VisibleTo(string? userId) =>
        e => e.Status != EventStatus.Draft || e.OrganizerId == userId;

    /// <summary>Hides drafts from anyone but their organizer, as if they did not exist.</summary>
    public static void EnsureVisible(Event @event, ICurrentUser user)
    {
        if (@event.Status == EventStatus.Draft && !@event.IsOrganizedBy(user.Id))
        {
            throw new NotFoundException(nameof(Event), @event.Id);
        }
    }

    /// <summary>Only an event's own organizer may change it.</summary>
    public static void EnsureCanManage(Event @event, ICurrentUser user)
    {
        EnsureVisible(@event, user);
        if (!@event.IsOrganizedBy(user.Id))
        {
            throw new ForbiddenAccessException("Only the event's organizer can manage it.");
        }
    }
}
