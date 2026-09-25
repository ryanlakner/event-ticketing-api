namespace Ticketing.Domain.Common;

public abstract class Entity
{
    public Guid Id { get; protected init; } = Guid.CreateVersion7();

    public DateTimeOffset CreatedAt { get; protected init; }

    public DateTimeOffset? UpdatedAt { get; private set; }

    /// <summary>
    /// Optimistic concurrency token. Every state change issues a new value, so two requests that
    /// read the same version cannot both save: the second fails instead of silently overwriting.
    /// </summary>
    public Guid Version { get; private set; } = Guid.NewGuid();

    protected void MarkModified(DateTimeOffset now)
    {
        UpdatedAt = now;
        Version = Guid.NewGuid();
    }
}
