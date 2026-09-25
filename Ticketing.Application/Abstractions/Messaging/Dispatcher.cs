using System.Collections.Concurrent;
using FluentValidation;
using Microsoft.Extensions.DependencyInjection;

namespace Ticketing.Application.Abstractions.Messaging;

internal sealed class Dispatcher(IServiceProvider serviceProvider) : IDispatcher
{
    // One closed wrapper per request type, so reflection only happens on first dispatch.
    private static readonly ConcurrentDictionary<Type, object> Wrappers = new();

    public Task<TResult> SendAsync<TResult>(
        ICommand<TResult> command,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(command);

        var wrapper = GetWrapper<TResult>(command.GetType(), typeof(CommandWrapper<,>));
        return wrapper.HandleAsync(command, serviceProvider, cancellationToken);
    }

    public Task<TResult> QueryAsync<TResult>(
        IQuery<TResult> query,
        CancellationToken cancellationToken = default
    )
    {
        ArgumentNullException.ThrowIfNull(query);

        var wrapper = GetWrapper<TResult>(query.GetType(), typeof(QueryWrapper<,>));
        return wrapper.HandleAsync(query, serviceProvider, cancellationToken);
    }

    private static HandlerWrapper<TResult> GetWrapper<TResult>(
        Type requestType,
        Type openWrapperType
    ) =>
        (HandlerWrapper<TResult>)
            Wrappers.GetOrAdd(
                requestType,
                static (type, openType) =>
                    Activator.CreateInstance(openType.MakeGenericType(type, typeof(TResult)))!,
                openWrapperType
            );

    private abstract class HandlerWrapper<TResult>
    {
        public abstract Task<TResult> HandleAsync(
            object request,
            IServiceProvider services,
            CancellationToken cancellationToken
        );

        protected static async Task ValidateAsync<TRequest>(
            TRequest request,
            IServiceProvider services,
            CancellationToken cancellationToken
        )
        {
            var failures = new List<FluentValidation.Results.ValidationFailure>();

            // Validators run sequentially because they may share a scoped DbContext.
            foreach (var validator in services.GetServices<IValidator<TRequest>>())
            {
                var result = await validator.ValidateAsync(request, cancellationToken);
                failures.AddRange(result.Errors);
            }

            if (failures.Count > 0)
            {
                throw new ValidationException(failures);
            }
        }
    }

    private sealed class CommandWrapper<TCommand, TResult> : HandlerWrapper<TResult>
        where TCommand : ICommand<TResult>
    {
        public override async Task<TResult> HandleAsync(
            object request,
            IServiceProvider services,
            CancellationToken cancellationToken
        )
        {
            var command = (TCommand)request;
            await ValidateAsync(command, services, cancellationToken);
            return await services
                .GetRequiredService<ICommandHandler<TCommand, TResult>>()
                .HandleAsync(command, cancellationToken);
        }
    }

    private sealed class QueryWrapper<TQuery, TResult> : HandlerWrapper<TResult>
        where TQuery : IQuery<TResult>
    {
        public override async Task<TResult> HandleAsync(
            object request,
            IServiceProvider services,
            CancellationToken cancellationToken
        )
        {
            var query = (TQuery)request;
            await ValidateAsync(query, services, cancellationToken);
            return await services
                .GetRequiredService<IQueryHandler<TQuery, TResult>>()
                .HandleAsync(query, cancellationToken);
        }
    }
}
