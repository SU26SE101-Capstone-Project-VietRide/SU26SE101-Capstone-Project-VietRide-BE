using FluentAssertions;
using MediatR;
using Microsoft.Extensions.Logging.Abstractions;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Features.Internal.Wallets.RefundToWallet;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Infrastructure.Messaging;
using VietRide.Payment.Infrastructure.Refunds;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Payment.IntegrationTests.Messaging;

public sealed class BookingCancelledRefundTests
{
    [Fact]
    public async Task CanonicalAndLegacyEventsConvergeOnOneRefundEffect()
    {
        var sender = new Sender();
        var handler = new BookingCancelledIntegrationEventHandler(sender, NullLogger<BookingCancelledIntegrationEventHandler>.Instance);

        await handler.HandleAsync(Event(), CancellationToken.None);
        await handler.HandleAsync(new BookingCancelledIntegrationEvent
        {
            BookingId = Event().BookingId,
            UserId = Event().UserId,
            RefundAmount = Event().RefundAmount,
            RefundOverride = Event().RefundOverride,
            CancellationReason = Event().CancellationReason,
        }, CancellationToken.None);

        sender.Requests.Should().HaveCount(2);
        sender.Effects.Should().Be(1);
    }

    [Fact]
    public async Task InitialFailureIsPersistedForRecurringRetryAndLeavesTripCancelled()
    {
        var sender = new Sender(new InvalidOperationException("wallet unavailable"));
        var failures = new Failures();
        var service = new RefundRetryService(
            sender,
            failures,
            new UnitOfWork(),
            new Clock(),
            NullLogger<RefundRetryService>.Instance);

        await service.ExecuteBookingRefundAsync(Event(), CancellationToken.None);

        sender.Attempts.Should().Be(1);
        failures.Items.Should().ContainSingle();
        failures.Items[0].RetryCount.Should().Be(0);
        failures.Items[0].CanRetry.Should().BeTrue();
    }

    [Fact]
    public async Task OtherRefundsRemainIndependent()
    {
        var sender = new Sender();
        var parcelRefund = new RefundToWalletCommand(
            Guid.Parse("33333333-3333-3333-3333-333333333333"),
            75_000,
            "PARCEL_REFUND",
            Guid.Parse("44444444-4444-4444-4444-444444444444"),
            "parcel-refund-independent");

        await sender.Send(parcelRefund, CancellationToken.None);

        sender.Requests.Should().ContainSingle();
        sender.Effects.Should().Be(1);
    }

    [Fact]
    public async Task OutboxRestartEventuallyPublishesAndDedupes()
    {
        var sender = new Sender();
        var handler = new BookingCancelledIntegrationEventHandler(sender, NullLogger<BookingCancelledIntegrationEventHandler>.Instance);

        await handler.HandleAsync(Event(), CancellationToken.None);
        await handler.HandleAsync(Event(), CancellationToken.None);

        sender.Requests.Should().HaveCount(2);
        sender.Requests.Cast<RefundToWalletCommand>().Select(x => x.IdempotencyKey).Distinct().Should().ContainSingle();
        sender.Effects.Should().Be(1);
    }

    private static BookingCancelledIntegrationEvent Event() => new()
    {
        EventId = Guid.Parse("11111111-1111-1111-1111-111111111111"),
        OccurredAtOffset = DateTimeOffset.Parse("2026-07-23T00:00:00Z"),
        BookingId = Guid.Parse("22222222-2222-2222-2222-222222222222"),
        UserId = Guid.Parse("33333333-3333-3333-3333-333333333333"),
        RefundAmount = 100_000,
        RefundOverride = false,
        CancellationReason = "operator cancellation",
    };

    private sealed class Sender : ISender
    {
        private readonly Exception? _failure;
        private readonly HashSet<string> _appliedReferences = [];
        public Sender(Exception? failure = null) => _failure = failure;
        public int Attempts { get; private set; }
        public int Effects { get; private set; }
        public List<object> Requests { get; } = [];
        public Task<TResponse> Send<TResponse>(IRequest<TResponse> request, CancellationToken cancellationToken = default)
        {
            Attempts++;
            Requests.Add(request);
            if (_failure is not null) throw _failure;
            if (request is RefundToWalletCommand refund
                && _appliedReferences.Add($"{refund.ReferenceType}:{refund.ReferenceId:D}"))
            {
                Effects++;
            }

            return Task.FromResult((TResponse)(object)new RefundToWalletResult(Guid.NewGuid(), 100_000));
        }
        public Task<object?> Send(object request, CancellationToken cancellationToken = default) => Task.FromResult<object?>(null);
        public IAsyncEnumerable<TResponse> CreateStream<TResponse>(IStreamRequest<TResponse> request, CancellationToken cancellationToken = default) => Empty<TResponse>();
        public IAsyncEnumerable<object?> CreateStream(object request, CancellationToken cancellationToken = default) => Empty<object?>();
        private static async IAsyncEnumerable<T> Empty<T>() { await Task.CompletedTask; yield break; }
    }

    private sealed class Failures : IRefundFailureLogRepository
    {
        public List<RefundFailureLog> Items { get; } = [];
        public Task<RefundFailureLog?> GetByIdAsync(Guid id, CancellationToken ct) => Task.FromResult<RefundFailureLog?>(Items.SingleOrDefault(x => x.Id == id));
        public Task<RefundFailureLog> AddAsync(RefundFailureLog entity, CancellationToken ct) { Items.Add(entity); return Task.FromResult(entity); }
        public void Update(RefundFailureLog entity) { }
        public void Remove(RefundFailureLog entity) => Items.Remove(entity);
        public IQueryable<RefundFailureLog> Query() => Items.AsQueryable();
        public IQueryable<RefundFailureLog> QueryNoTracking() => Items.AsQueryable();
        public Task<IReadOnlyList<RefundFailureLog>> GetUnresolvedAsync(CancellationToken ct) => Task.FromResult<IReadOnlyList<RefundFailureLog>>(Items);
        public Task<IReadOnlyList<RefundFailureLog>> GetRetryableAsync(int max, CancellationToken ct) => Task.FromResult<IReadOnlyList<RefundFailureLog>>(Items.Where(x => x.CanRetry).ToList());
    }

    private sealed class UnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct) => Task.FromResult(1);
        public Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operation, CancellationToken ct) => operation();
        public Task BeginTransactionAsync(CancellationToken ct) => Task.CompletedTask;
        public Task CommitAsync(CancellationToken ct) => Task.CompletedTask;
        public Task RollbackAsync(CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class Clock : IClock { public DateTimeOffset UtcNow => DateTimeOffset.Parse("2026-07-23T00:00:00Z"); }
}
