using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Ticketing.Domain.Events;
using Ticketing.Domain.Reservations;

namespace Ticketing.Infrastructure.Persistence.Configurations;

internal sealed class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    public void Configure(EntityTypeBuilder<Reservation> builder)
    {
        builder.ToTable(
            "Reservations",
            t => t.HasCheckConstraint("CK_Reservations_Quantity", "[Quantity] > 0")
        );

        builder.HasKey(r => r.Id);
        builder.Property(r => r.Id).ValueGeneratedNever();
        builder.Property(r => r.Version).IsConcurrencyToken();

        builder.Property(r => r.CustomerId).HasMaxLength(Event.UserIdMaxLength);
        builder.Property(r => r.CustomerEmail).HasMaxLength(Reservation.EmailMaxLength);
        builder.Property(r => r.Status).HasConversion<string>().HasMaxLength(20);
        builder.Ignore(r => r.IsActive);

        builder
            .HasOne<Event>()
            .WithMany()
            .HasForeignKey(r => r.EventId)
            .OnDelete(DeleteBehavior.Restrict);

        // Supports the expiry sweep: WHERE Status = 'Pending' AND ExpiresAt <= @now.
        builder.HasIndex(r => new { r.Status, r.ExpiresAt });
        builder.HasIndex(r => r.EventId);
        builder.HasIndex(r => r.CustomerId);
    }
}
