namespace Ticketing.Application.Abstractions.Identity;

/// <summary>The caller of the current request, resolved from its access token.</summary>
public interface ICurrentUser
{
    /// <summary>Stable user ID (the Entra ID object ID), or <c>null</c> for anonymous callers.</summary>
    string? Id { get; }
}
