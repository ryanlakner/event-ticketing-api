namespace Ticketing.Application.Common.Exceptions;

/// <summary>The caller is signed in but not allowed to perform this operation.</summary>
public sealed class ForbiddenAccessException(string message) : Exception(message);
