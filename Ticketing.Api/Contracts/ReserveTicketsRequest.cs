namespace Ticketing.Api.Contracts;

/// <summary>Payload for holding seats at an event.</summary>
/// <param name="CustomerEmail">Who the tickets are for.</param>
/// <param name="Quantity">Number of seats (1-10).</param>
public sealed record ReserveTicketsRequest(string CustomerEmail, int Quantity);
