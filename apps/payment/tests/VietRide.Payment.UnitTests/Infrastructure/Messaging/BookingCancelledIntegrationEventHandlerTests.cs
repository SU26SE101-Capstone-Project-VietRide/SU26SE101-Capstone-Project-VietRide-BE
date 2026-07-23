using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using VietRide.Payment.Application.Features.Internal.Wallets.RefundToWallet;
using VietRide.Payment.Infrastructure.Messaging;

namespace VietRide.Payment.UnitTests.Infrastructure.Messaging;

public sealed class BookingCancelledIntegrationEventHandlerTests
{
    [Fact]
    public async Task HandleAsync_WhenBookingCancelled_UsesPayloadUserIdAndBookingReference()
    {
        var sender = new CapturingSender();
        var handler = CreateHandler(sender);
        var integrationEvent = CreateEvent();

        await handler.HandleAsync(integrationEvent, CancellationToken.None);

        var command = sender.LastRequest.Should().BeOfType<RefundToWalletCommand>().Subject;
        command.UserId.Should().Be(integrationEvent.UserId!.Value);
        command.Amount.Should().Be(integrationEvent.RefundAmount!.Value);
        command.ReferenceType.Should().Be("BOOKING_REFUND");
        command.ReferenceId.Should().Be(integrationEvent.BookingId!.Value);
        command.IdempotencyKey.Should().Be(integrationEvent.EventId!.Value.ToString("D"));
    }

    [Fact]
    public async Task HandleAsync_WhenRefundAmountIsZero_DoesNotSendRefundCommand()
    {
        // A PENDING_PAYMENT cancellation carries refundAmount=0; RefundToWalletCommandValidator
        // requires Amount > 0, so the handler must skip the credit rather than dead-letter.
        var sender = new CapturingSender();
        var handler = CreateHandler(sender);
        var zeroRefundEvent = new BookingCancelledIntegrationEvent
        {
            BookingId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            UserId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            RefundAmount = 0,
            RefundOverride = false,
            CancellationReason = "Passenger cancellation",
        };

        await handler.HandleAsync(zeroRefundEvent, CancellationToken.None);

        sender.Requests.Should().BeEmpty();
    }

    [Fact]
    public async Task HandleAsync_WhenEventIsRedelivered_SendsSameReferenceSoRefundUseCaseCanNoOp()
    {
        var sender = new CapturingSender();
        var handler = CreateHandler(sender);
        var integrationEvent = CreateEvent();

        await handler.HandleAsync(integrationEvent, CancellationToken.None);
        await handler.HandleAsync(integrationEvent, CancellationToken.None);

        sender.Requests.Should().HaveCount(2);
        sender.Requests
            .Cast<RefundToWalletCommand>()
            .Should()
            .OnlyContain(command =>
                command.ReferenceType == "BOOKING_REFUND"
                && command.ReferenceId == integrationEvent.BookingId!.Value
                && command.UserId == integrationEvent.UserId!.Value);
    }

    [Fact]
    public async Task HandleAsync_WhenTransientRefundUseCaseFailureOccurs_RetriesBeforeAck()
    {
        var sender = new CapturingSender(
            new TimeoutException("Transient database timeout."),
            new TimeoutException("Transient database timeout."));
        var handler = CreateHandler(sender);

        await handler.HandleAsync(CreateEvent(), CancellationToken.None);

        sender.Requests.Should().HaveCount(3);
    }

    [Fact]
    public async Task HandleAsync_WhenRefundUseCaseFails_PropagatesForConsumerDeadLetter()
    {
        var exception = new InvalidOperationException("Permanent refund failure.");
        var sender = new CapturingSender(exception, exception, exception);
        var handler = CreateHandler(sender);

        var act = async () => await handler.HandleAsync(CreateEvent(), CancellationToken.None);

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Permanent refund failure.");
        sender.Requests.Should().HaveCount(3);
    }

    private static BookingCancelledIntegrationEventHandler CreateHandler(CapturingSender sender)
        => new(sender, NullLogger<BookingCancelledIntegrationEventHandler>.Instance);

    private static BookingCancelledIntegrationEvent CreateEvent()
        => new()
        {
            EventId = Guid.NewGuid(),
            OccurredAtOffset = DateTimeOffset.Parse("2026-07-17T00:00:00Z"),
            BookingId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
            UserId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
            RefundAmount = 175_000,
            RefundOverride = false,
            CancellationReason = "Passenger cancellation",
        };

    private sealed class CapturingSender : ISender
    {
        private readonly Queue<Exception> _exceptions;

        public CapturingSender(params Exception[] exceptions)
        {
            _exceptions = new Queue<Exception>(exceptions);
        }

        public List<object> Requests { get; } = [];

        public object? LastRequest => Requests.LastOrDefault();

        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);

            if (_exceptions.TryDequeue(out var exception))
            {
                throw exception;
            }

            if (request is RefundToWalletCommand)
            {
                return Task.FromResult((TResponse)(object)new RefundToWalletResult(Guid.NewGuid(), 1_175_000));
            }

            throw new NotSupportedException(request.GetType().Name);
        }

        public Task<object?> Send(object request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);

            if (_exceptions.TryDequeue(out var exception))
            {
                throw exception;
            }

            return Task.FromResult<object?>(null);
        }

        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(
            IStreamRequest<TResponse> request,
            CancellationToken cancellationToken = default)
            => EmptyAsync<TResponse>();

        public IAsyncEnumerable<object?> CreateStream(
            object request,
            CancellationToken cancellationToken = default)
            => EmptyAsync<object?>();

        private static async IAsyncEnumerable<TResponse> EmptyAsync<TResponse>()
        {
            await Task.CompletedTask;
            yield break;
        }
    }
}
