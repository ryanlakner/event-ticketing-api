namespace Ticketing.Application.Common.Security;

/// <summary>App roles defined on the Entra ID app registration (see infra/bootstrap).</summary>
public static class Roles
{
    /// <summary>Creates and manages their own events.</summary>
    public const string Organizer = "Organizer";

    /// <summary>Reserves and confirms tickets.</summary>
    public const string Customer = "Customer";
}
