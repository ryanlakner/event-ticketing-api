using System.Security.Claims;
using Ticketing.Application.Abstractions.Identity;

namespace Ticketing.Api.Identity;

/// <summary>Resolves the caller from the validated access token on the current request.</summary>
internal sealed class HttpCurrentUser(IHttpContextAccessor accessor) : ICurrentUser
{
    // Entra ID's stable, tenant-wide user ID ("oid"), as mapped by the JWT handler.
    private const string ObjectIdClaim =
        "http://schemas.microsoft.com/identity/claims/objectidentifier";

    public string? Id
    {
        get
        {
            var user = accessor.HttpContext?.User;
            if (user?.Identity?.IsAuthenticated != true)
            {
                return null;
            }

            // Tokens from `dotnet user-jwts` (local development) have no "oid", only "sub".
            return user.FindFirstValue(ObjectIdClaim)
                ?? user.FindFirstValue(ClaimTypes.NameIdentifier);
        }
    }
}
