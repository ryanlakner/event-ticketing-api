using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Ticketing.Domain.Events;
using Ticketing.Domain.Reservations;

namespace Ticketing.Application.Abstractions.Data;

public interface IApplicationDbContext
{
    DbSet<Event> Events { get; }

    DbSet<Reservation> Reservations { get; }

    ChangeTracker ChangeTracker { get; }

    Task<int> SaveChangesAsync(CancellationToken cancellationToken = default);
}
