using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using VietRide.Payment.Infrastructure.DependencyInjection;
using VietRide.Payment.Infrastructure.Messaging;
using VietRide.Shared.Messaging.Abstractions;
using VietRide.Shared.Messaging.RabbitMq;

namespace VietRide.Payment.IntegrationTests;

public sealed class BookingCancelledConsumerRegistrationTests
{
    [Fact]
    public void AddInfrastructure_WhenConsumersAreEnabled_RegistersBookingCancelledConsumer()
    {
        var services = new ServiceCollection();
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] = "Host=localhost;Port=5432;Database=test;Username=postgres;Password=postgres",
            })
            .Build();
        services.AddLogging();
        services.AddSingleton<ISender, NoOpSender>();

        services.AddInfrastructure(configuration, registerConsumers: true);

        var hasHostedService = services.Any(descriptor =>
            descriptor.ServiceType == typeof(IHostedService)
            && descriptor.ImplementationType == typeof(RabbitMqConsumerBackgroundService<BookingCancelledIntegrationEvent>));

        using var provider = services.BuildServiceProvider();
        var handler = provider.GetRequiredService<IIntegrationEventHandler<BookingCancelledIntegrationEvent>>();
        var options = provider.GetRequiredService<IOptions<RabbitMqConsumerOptions<BookingCancelledIntegrationEvent>>>();

        handler.Should().BeOfType<BookingCancelledIntegrationEventHandler>();
        options.Value.Value.QueueName.Should().Be("payment.booking-refund");
        options.Value.Value.BindingKeys.Should().ContainSingle().Which.Should().Be(BookingCancelledIntegrationEvent.EventType);
        hasHostedService.Should().BeTrue();
    }

    private sealed class NoOpSender : ISender
    {
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
            => Task.FromResult(default(TResponse)!);

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
            => Task.FromResult<object?>(null);

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default)
            => EmptyAsync<TResponse>();

        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default)
            => EmptyAsync<object?>();

        private static async IAsyncEnumerable<TResponse> EmptyAsync<TResponse>()
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
