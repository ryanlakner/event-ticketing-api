namespace Ticketing.Application.Reservations;

public sealed class ReservationOptions
{
    public const string SectionName = "Reservations";

    /// <summary>How long seats are held before an unconfirmed reservation expires.</summary>
    public TimeSpan HoldDuration { get; set; } = TimeSpan.FromMinutes(10);

    public bool ExpirySweepEnabled { get; set; } = true;

    public TimeSpan ExpirySweepInterval { get; set; } = TimeSpan.FromMinutes(1);

    public int ExpiryBatchSize { get; set; } = 100;
}
