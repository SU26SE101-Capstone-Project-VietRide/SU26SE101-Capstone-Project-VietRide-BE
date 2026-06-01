using System.Reflection;
using FluentValidation;
using MediatR;
using Microsoft.Extensions.DependencyInjection;
using VietRide.Shared.Application.Behaviors;

namespace VietRide.Shared.Application.DependencyInjection;

/// <summary>
/// Extension methods for registering the shared MediatR pipeline behaviors
/// (Logging → Validation → Transaction) and FluentValidation validators from
/// the caller's assembly.
/// </summary>
public static class ApplicationServiceCollectionExtensions
{
    /// <summary>
    /// Registers MediatR handlers and pipeline behaviors from <paramref name="handlerAssemblies"/>,
    /// plus FluentValidation validators discovered from <paramref name="validatorAssemblies"/>.
    /// Pipeline order: <see cref="LoggingBehavior{TRequest,TResponse}"/> →
    /// <see cref="ValidationBehavior{TRequest,TResponse}"/> →
    /// <see cref="TransactionBehavior{TRequest,TResponse}"/>.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="handlerAssemblies">
    /// Assemblies that contain MediatR <c>IRequestHandler</c> implementations.
    /// </param>
    /// <param name="validatorAssemblies">
    /// Assemblies that contain FluentValidation <c>AbstractValidator</c> implementations.
    /// Pass the same assemblies as <paramref name="handlerAssemblies"/> when validators
    /// live alongside handlers.
    /// </param>
    public static IServiceCollection AddVietRideMediatRBehaviors(
        this IServiceCollection services,
        Assembly[] handlerAssemblies,
        Assembly[]? validatorAssemblies = null)
    {
        // MediatR v11 — AddMediatR takes params Assembly[] directly.
        // (v12+ introduced the lambda cfg overload with RegisterServicesFromAssembly,
        // but v12 is commercial and pinned out — AGENTS.md invariant.)
        services.AddMediatR(handlerAssemblies);

        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(LoggingBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(ValidationBehavior<,>));
        services.AddTransient(typeof(IPipelineBehavior<,>), typeof(TransactionBehavior<,>));

        var assembliesForValidators = validatorAssemblies ?? handlerAssemblies;
        foreach (var assembly in assembliesForValidators)
        {
            services.AddValidatorsFromAssembly(assembly);
        }

        return services;
    }
}
