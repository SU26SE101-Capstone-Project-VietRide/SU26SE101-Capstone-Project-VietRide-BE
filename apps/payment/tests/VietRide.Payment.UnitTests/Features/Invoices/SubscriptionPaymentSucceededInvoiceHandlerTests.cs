using FluentAssertions;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Features.Invoices;
using VietRide.Payment.Application.Models;
using VietRide.Payment.Domain.Entities;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Payment.UnitTests.Features.Invoices;

public sealed class SubscriptionPaymentSucceededInvoiceHandlerTests
{
    [Fact]
    public async Task HandleAsync_ReplayAndDuplicatePayment_CreateOneInvoiceAndConsumeOneCounterValue()
    {
        var invoices = new FakeInvoiceRepository();
        var counters = new FakeCounterRepository();
        var processed = new FakeProcessedEventRepository();
        var jobs = new FakeJobScheduler();
        var handler = new SubscriptionPaymentSucceededInvoiceHandler(
            invoices,
            counters,
            processed,
            jobs,
            new FakeUnitOfWork(),
            new FrozenClock(new DateTimeOffset(2026, 7, 14, 0, 0, 0, TimeSpan.Zero)));
        var paymentId = Guid.NewGuid();
        var first = CreateEvent(Guid.NewGuid(), paymentId);

        await handler.HandleAsync(first, CancellationToken.None);
        await handler.HandleAsync(first, CancellationToken.None);
        await handler.HandleAsync(CreateEvent(Guid.NewGuid(), paymentId), CancellationToken.None);

        invoices.Items.Should().ContainSingle();
        invoices.Items[0].InvoiceNumber.Should().Be("VR-INV-202607-000001");
        invoices.Items[0].PdfGenerationAttempts.Should().Be(0);
        counters.CallCount.Should().Be(1);
        jobs.InvoiceIds.Should().ContainSingle().Which.Should().Be(invoices.Items[0].Id);
        processed.Items.Should().HaveCount(2);
    }

    [Fact]
    public void InvoiceNumberPeriod_AtVietnamMonthBoundary_UsesVietnamCalendarMonth()
    {
        var instant = new DateTimeOffset(2026, 7, 31, 17, 30, 0, TimeSpan.Zero);

        InvoiceNumberPeriod.FromInstant(instant).Should().Be("202608");
    }

    private static SubscriptionPaymentSucceededInvoiceEvent CreateEvent(Guid eventId, Guid paymentId)
        => new(
            eventId,
            new DateTime(2026, 7, 14, 0, 0, 0, DateTimeKind.Utc),
            paymentId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            Guid.NewGuid(),
            500_000,
            "WALLET",
            "Business",
            "MONTHLY",
            new DateTimeOffset(2026, 7, 14, 0, 0, 0, TimeSpan.Zero),
            new DateTimeOffset(2026, 8, 14, 0, 0, 0, TimeSpan.Zero),
            new SubscriptionBuyerSnapshotV1(
                "Nhà xe Việt",
                "BR-001",
                "0312345678",
                "billing@example.test",
                "0900000000",
                "1 Nguyễn Huệ",
                null,
                "TP.HCM"));

    private sealed class FakeInvoiceRepository : IInvoiceRepository
    {
        public List<Invoice> Items { get; } = [];
        public Task<Invoice?> FindByPaymentIdAsync(Guid paymentId, CancellationToken cancellationToken)
            => Task.FromResult(Items.SingleOrDefault(item => item.PaymentId == paymentId));
        public Task<Invoice?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(Items.SingleOrDefault(item => item.Id == id));
        public Task<Invoice> AddAsync(Invoice entity, CancellationToken cancellationToken = default)
        {
            Items.Add(entity);
            return Task.FromResult(entity);
        }
        public void Update(Invoice entity) => throw new NotSupportedException();
        public void Remove(Invoice entity) => throw new NotSupportedException();
        public IQueryable<Invoice> Query() => Items.AsQueryable();
        public IQueryable<Invoice> QueryNoTracking() => Query();
    }

    private sealed class FakeCounterRepository : IInvoiceNumberCounterRepository
    {
        public int CallCount { get; private set; }
        public Task<long> NextAsync(string periodKey, CancellationToken cancellationToken)
        {
            CallCount++;
            return Task.FromResult((long)CallCount);
        }
    }

    private sealed class FakeProcessedEventRepository : IProcessedIntegrationEventRepository
    {
        public List<ProcessedIntegrationEvent> Items { get; } = [];
        public Task<bool> ExistsAsync(string consumer, Guid eventId, CancellationToken cancellationToken)
            => Task.FromResult(Items.Any(item => item.Consumer == consumer && item.EventId == eventId));
        public Task<ProcessedIntegrationEvent?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(Items.SingleOrDefault(item => item.Id == id));
        public Task<ProcessedIntegrationEvent> AddAsync(ProcessedIntegrationEvent entity, CancellationToken cancellationToken = default)
        {
            Items.Add(entity);
            return Task.FromResult(entity);
        }
        public void Update(ProcessedIntegrationEvent entity) => throw new NotSupportedException();
        public void Remove(ProcessedIntegrationEvent entity) => throw new NotSupportedException();
        public IQueryable<ProcessedIntegrationEvent> Query() => Items.AsQueryable();
        public IQueryable<ProcessedIntegrationEvent> QueryNoTracking() => Query();
    }

    private sealed class FakeJobScheduler : IInvoiceJobScheduler
    {
        public List<Guid> InvoiceIds { get; } = [];
        public void EnqueuePdfGeneration(Guid invoiceId) => InvoiceIds.Add(invoiceId);
    }

    private sealed class FrozenClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class FakeUnitOfWork : IUnitOfWork
    {
        public Task<int> SaveChangesAsync(CancellationToken ct) => Task.FromResult(1);
        public Task<T> ExecuteInTransactionAsync<T>(Func<Task<T>> operation, CancellationToken ct) => operation();
        public Task BeginTransactionAsync(CancellationToken ct) => Task.CompletedTask;
        public Task CommitAsync(CancellationToken ct) => Task.CompletedTask;
        public Task RollbackAsync(CancellationToken ct) => Task.CompletedTask;
    }
}
