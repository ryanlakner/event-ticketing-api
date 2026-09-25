namespace Ticketing.Application.Abstractions.Messaging;

/// <summary>A request that reads state without side effects and returns <typeparamref name="TResult"/>.</summary>
public interface IQuery<TResult>;
