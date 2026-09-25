using Microsoft.EntityFrameworkCore;
using Ticketing.Application.Abstractions.Data;
using Ticketing.Domain.Events;
using Ticketing.Domain.Reservations;

namespace Ticketing.Infrastructure.Persistence;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options)
    : DbContext(options),
        IApplicationDbContext
{
    public DbSet<Event> Events => Set<Event>();

    public DbSet<Reservation> Reservations => Set<Reservation>();

    protected override void OnModelCreating(ModelBuilder modelBuilder) =>
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);
}
