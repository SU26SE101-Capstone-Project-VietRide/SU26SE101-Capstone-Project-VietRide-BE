using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VietRide.Shared.Messaging.Abstractions;
using VietRide.Shared.Messaging.Outbox;
using VietRide.Shared.Messaging.RabbitMq;

namespace VietRide.Shared.Messaging.DependencyInjection;

/// <summary>
/// Composition-root helpers for VietRide.Shared.Messaging.
/// </summary>
public static class MessagingServiceCollectionExtensions
{
    /// <summary>
    /// Registers RabbitMQ connection factory (singleton), <see cref="IEventPublisher"/>
    /// (singleton — channels are cheap, connection is shared) and the
    /// outbox background worker. Services MUST additionally register their
    /// own <see cref="VietRide.Shared.Persistence.Outbox.IOutboxStore"/>
    /// implementation in their Infrastructure layer; without it the worker idles.
    /// </summary>
    public static IServiceCollection AddVietRideMessaging(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddOptions<RabbitMqOptions>()
            .Bind(configuration.GetSection(RabbitMqOptions.SectionName))
            .ValidateOnStart();

        services.AddOptions<OutboxOptions>()
            .Bind(configuration.GetSection(OutboxOptions.SectionName))
            .ValidateOnStart();

        services.AddSingleton<IRabbitMqConnectionFactory, RabbitMqConnectionFactory>();
        services.AddSingleton<IEventPublisher, RabbitMqEventPublisher>();

        services.AddSingleton<OutboxBackgroundService>();
        services.AddSingleton<IOutboxPublisher>(sp => sp.GetRequiredService<OutboxBackgroundService>());
        services.AddHostedService(sp => sp.GetRequiredService<OutboxBackgroundService>());

        return services;
    }
}
