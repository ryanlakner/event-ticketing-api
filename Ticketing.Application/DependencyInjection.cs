using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Ticketing.Application.Abstractions.Messaging;
using Ticketing.Application.Reservations;

namespace Ticketing.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        var assembly = typeof(DependencyInjection).Assembly;

        services.TryAddSingleton(TimeProvider.System);
        services.AddOptions<ReservationOptions>();
        services.AddScoped<IDispatcher, Dispatcher>();
        services.AddValidatorsFromAssembly(assembly, includeInternalTypes: true);

        Type[] handlerTypes = [typeof(ICommandHandler<,>), typeof(IQueryHandler<,>)];
        var registrations =
            from type in assembly.DefinedTypes
            where type is { IsAbstract: false, IsInterface: false }
            from service in type.ImplementedInterfaces
            where service.IsGenericType && handlerTypes.Contains(service.GetGenericTypeDefinition())
            select (service, implementation: type.AsType());

        foreach (var (service, implementation) in registrations)
        {
            services.AddScoped(service, implementation);
        }

        return services;
    }
}
