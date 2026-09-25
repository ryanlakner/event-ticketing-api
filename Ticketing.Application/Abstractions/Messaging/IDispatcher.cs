namespace Ticketing.Application.Abstractions.Messaging;

/// <summary>Routes commands and queries to their handlers, validating them first.</summary>
public interface IDispatcher
{
    Task<TResult> SendAsync<TResult>(
        ICommand<TResult> command,
        CancellationToken cancellationToken = default
    );

    Task<TResult> QueryAsync<TResult>(
        IQuery<TResult> query,
        CancellationToken cancellationToken = default
    );
}
