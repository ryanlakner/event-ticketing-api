using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ticketing.Domain.Events;

namespace Ticketing.Infrastructure.Persistence.Configurations;

internal sealed class EventConfiguration : IEntityTypeConfiguration<Event>
{
    public void Configure(EntityTypeBuilder<Event> builder)
    {
        // Last line of defence against overselling: the database itself rejects the write.
        builder.ToTable(
            "Events",
            t =>
                t.HasCheckConstraint(
                    "CK_Events_SeatsReserved",
                    "[SeatsReserved] >= 0 AND [SeatsReserved] <= [Capacity]"
                )
        );

        builder.HasKey(e => e.Id);
        builder.Property(e => e.Id).ValueGeneratedNever();
        builder.Property(e => e.Version).IsConcurrencyToken();

        builder.Property(e => e.Name).HasMaxLength(Event.NameMaxLength);
        builder.Property(e => e.Description).HasMaxLength(Event.DescriptionMaxLength);
        builder.Property(e => e.Venue).HasMaxLength(Event.VenueMaxLength);
        builder.Property(e => e.Status).HasConversion<string>().HasMaxLength(20);
        builder.Ignore(e => e.SeatsAvailable);

        builder.HasIndex(e => new { e.Status, e.StartsAt });
    }
}
