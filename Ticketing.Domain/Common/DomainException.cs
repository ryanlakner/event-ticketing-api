namespace Ticketing.Domain.Common;

/// <summary>Raised when an operation conflicts with the current state of the domain.</summary>
public sealed class DomainException(string message) : Exception(message);
