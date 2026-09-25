namespace Ticketing.Application.Abstractions.Messaging;

/// <summary>A request that changes state and returns <typeparamref name="TResult"/>.</summary>
public interface ICommand<TResult>;

/// <summary>A command that produces no meaningful result.</summary>
public interface ICommand : ICommand<Unit>;
