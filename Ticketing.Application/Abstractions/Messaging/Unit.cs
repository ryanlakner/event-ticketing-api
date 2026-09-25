namespace Ticketing.Application.Abstractions.Messaging;

/// <summary>Represents a void result for commands.</summary>
public readonly record struct Unit
{
    public static readonly Unit Value;
}
