using FluentAssertions;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Infrastructure.DependencyInjection;
using VietRide.Booking.Infrastructure.Http;
using VietRide.Booking.Infrastructure.Messaging;
using VietRide.Shared.Messaging.DependencyInjection;
using VietRide.Shared.Messaging.RabbitMq;

namespace VietRide.Booking.IntegrationTests;

public sealed class PaymentEventConsumerIntegrationTests
{
    [Fact]
    public void AddInfrastructure_WithConsumers_BindsPaymentEventQueues()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Trip:UseDevStub"] = "true",
                ["Identity:UseDevStub"] = "true",
                ["Payment:UseDevStub"] = "true",
                ["REDIS_URL"] = "localhost:6379",
                ["RabbitMq:ExchangeName"] = "vietride.events",
            })
            .Build();
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddVietRideMessaging(configuration);
        services.AddInfrastructure(configuration, registerConsumers: true);

        using var provider = services.BuildServiceProvider();

        AssertConsumer<PaymentSucceededIntegrationEvent>(
            provider,
            "booking.payment-succeeded",
            "payment.payment.succeeded");
        AssertConsumer<PaymentExpiredIntegrationEvent>(
            provider,
            "booking.payment-expired",
            "payment.payment.expired");
        AssertConsumer<WalletCreditedIntegrationEvent>(
            provider,
            "booking.wallet-credited",
            "payment.wallet.credited");
    }

    [Fact]
    public void AddInfrastructure_WithIdentityDevStub_BindsOperatorServiceClient()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Trip:UseDevStub"] = "true",
                ["Identity:UseDevStub"] = "true",
                ["Payment:UseDevStub"] = "true",
                ["REDIS_URL"] = "localhost:6379",
            })
            .Build();
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddInfrastructure(configuration, registerConsumers: false);

        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<IOperatorServiceClient>()
            .Should().BeOfType<DevOperatorServiceClient>();
    }

    private static void AssertConsumer<TEvent>(
        IServiceProvider provider,
        string expectedQueueName,
        string expectedBindingKey)
        where TEvent : VietRide.Shared.Messaging.Abstractions.IIntegrationEvent
    {
        var options = provider.GetRequiredService<IOptions<RabbitMqConsumerOptions<TEvent>>>().Value.Value;

        options.QueueName.Should().Be(expectedQueueName);
        options.BindingKeys.Should().ContainSingle().Which.Should().Be(expectedBindingKey);
    }
}
