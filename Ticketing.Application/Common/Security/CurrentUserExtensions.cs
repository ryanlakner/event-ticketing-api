using Ticketing.Application.Abstractions.Identity;
using Ticketing.Application.Common.Exceptions;

namespace Ticketing.Application.Common.Security;

internal static class CurrentUserExtensions
{
    /// <summary>
    /// The caller's ID for operations that record ownership. Controllers already require sign-in
    /// for these, so an anonymous caller here means a missing [Authorize], and we fail closed.
    /// </summary>
    public static string RequireId(this ICurrentUser user) =>
        user.Id ?? throw new ForbiddenAccessException("You must be signed in.");
}
