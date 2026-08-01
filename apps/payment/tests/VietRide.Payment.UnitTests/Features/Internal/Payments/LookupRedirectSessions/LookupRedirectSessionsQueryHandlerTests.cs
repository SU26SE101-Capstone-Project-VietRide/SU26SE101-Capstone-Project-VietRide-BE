using FluentAssertions;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Abstractions.Services;
using VietRide.Payment.Application.Features.Internal.Payments.LookupRedirectSessions;
using VietRide.Payment.Application.Models;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using PaymentEntity = VietRide.Payment.Domain.Entities.Payment;

namespace VietRide.Payment.UnitTests.Features.Internal.Payments.LookupRedirectSessions;

public sealed class LookupRedirectSessionsQueryHandlerTests
{
    private static readonly DateTimeOffset Now = DateTimeOffset.Parse("2026-08-01T08:00:00Z");

    [Fact]
    public async Task Handle_WhenCandidatesAreEligible_PreservesRequestOrderAndReturnsAmount()
    {
        var userId = Guid.NewGuid();
        var firstReferenceId = Guid.NewGuid();
        var secondReferenceId = Guid.NewGuid();
        var repository = new FakePaymentRepository(
        [
            Candidate(PaymentReferenceType.PARCEL, secondReferenceId, userId, amount: 220_000),
            Candidate(PaymentReferenceType.BOOKING, firstReferenceId, userId, amount: 110_000),
        ]);
        var handler = CreateHandler(repository);
        var query = new LookupRedirectSessionsQuery(
            userId,
            [new("BOOKING", firstReferenceId), new("PARCEL", secondReferenceId)]);

        var result = await handler.Handle(query, CancellationToken.None);

        result.Select(item => item.ReferenceId).Should().Equal(firstReferenceId, secondReferenceId);
        result.Select(item => item.Amount).Should().Equal(110_000, 220_000);
        repository.LookupCallCount.Should().Be(1);
        repository.LastReferences.Should().Equal(
            new PaymentReference(PaymentReferenceType.BOOKING, firstReferenceId),
            new PaymentReference(PaymentReferenceType.PARCEL, secondReferenceId));
    }

    [Fact]
    public async Task Handle_WhenCandidatesAreIneligible_OmitsEveryCandidateWithoutFallback()
    {
        var userId = Guid.NewGuid();
        var candidates = new[]
        {
            Candidate(PaymentReferenceType.BOOKING, Guid.NewGuid(), Guid.NewGuid()),
            Candidate(PaymentReferenceType.BOOKING, Guid.NewGuid(), userId) with { Method = PaymentMethod.WALLET },
            Candidate(PaymentReferenceType.BOOKING, Guid.NewGuid(), userId) with { Status = PaymentStatus.SUCCEEDED },
            Candidate(PaymentReferenceType.BOOKING, Guid.NewGuid(), userId) with { DueAt = null },
            Candidate(PaymentReferenceType.BOOKING, Guid.NewGuid(), userId) with { DueAt = Now },
            Candidate(PaymentReferenceType.BOOKING, Guid.NewGuid(), userId) with { PaymentRedirectUrl = " " },
            Candidate(PaymentReferenceType.BOOKING, Guid.NewGuid(), userId) with { Context = "{" },
            Candidate(PaymentReferenceType.BOOKING, Guid.NewGuid(), userId) with { ContextReconciliationRequired = true },
        };
        var repository = new FakePaymentRepository(candidates);
        var handler = CreateHandler(repository);
        var query = new LookupRedirectSessionsQuery(
            userId,
            candidates.Select(candidate => new LookupRedirectSessionsQuery.Reference(
                candidate.ReferenceType.ToString(),
                candidate.ReferenceId)).ToArray());

        var result = await handler.Handle(query, CancellationToken.None);

        result.Should().BeEmpty();
        repository.LookupCallCount.Should().Be(1);
    }

    [Fact]
    public async Task Handle_WhenContextEconomicsDoNotMatchPayment_OmitsCandidate()
    {
        var userId = Guid.NewGuid();
        var referenceId = Guid.NewGuid();
        var candidate = Candidate(PaymentReferenceType.BOOKING, referenceId, userId, amount: 100_000);
        candidate = candidate with { Amount = 100_001 };
        var handler = CreateHandler(new FakePaymentRepository([candidate]));

        var result = await handler.Handle(
            new LookupRedirectSessionsQuery(userId, [new("BOOKING", referenceId)]),
            CancellationToken.None);

        result.Should().BeEmpty();
    }

    private static LookupRedirectSessionsQueryHandler CreateHandler(FakePaymentRepository repository)
        => new(repository, new AcceptingUrlValidator(), new FrozenClock(Now));

    private static RedirectSessionLookupCandidate Candidate(
        PaymentReferenceType referenceType,
        Guid referenceId,
        Guid userId,
        long amount = 100_000)
    {
        PaymentContextV1 context = referenceType == PaymentReferenceType.BOOKING_GROUP
            ? new(1,
            [
                Allocation(PaymentReferenceType.BOOKING, Guid.NewGuid(), amount / 2),
                Allocation(PaymentReferenceType.BOOKING, Guid.NewGuid(), amount - (amount / 2)),
            ])
            : new(1, [Allocation(referenceType, referenceId, amount)]);
        var contextJson = PaymentContextCodec.ValidateAndSerialize(
            context,
            referenceType.ToString(),
            referenceId,
            amount);
        return new RedirectSessionLookupCandidate(
            Guid.NewGuid(),
            referenceType,
            referenceId,
            userId,
            amount,
            PaymentMethod.VNPAY,
            PaymentStatus.PENDING_REDIRECT,
            Now.AddMinutes(1),
            "https://sandbox.vnpayment.vn/paymentv2/vpcpay.html?signed=secret",
            contextJson,
            false);
    }

    private static PaymentAllocationV1 Allocation(
        PaymentReferenceType referenceType,
        Guid referenceId,
        long amount)
        => new(referenceId, referenceType.ToString(), Guid.NewGuid(), Guid.NewGuid(), amount, 0, 0);

    private sealed class AcceptingUrlValidator : IVnPayRedirectUrlValidator
    {
        public bool IsTrusted(string? paymentRedirectUrl) => !string.IsNullOrWhiteSpace(paymentRedirectUrl);
    }

    private sealed class FrozenClock(DateTimeOffset utcNow) : IClock
    {
        public DateTimeOffset UtcNow { get; } = utcNow;
    }

    private sealed class FakePaymentRepository(IReadOnlyList<RedirectSessionLookupCandidate> candidates)
        : IPaymentRepository
    {
        public int LookupCallCount { get; private set; }
        public IReadOnlyList<PaymentReference> LastReferences { get; private set; } = [];

        public Task<IReadOnlyList<RedirectSessionLookupCandidate>> ListLatestRedirectSessionCandidatesAsync(
            IReadOnlyCollection<PaymentReference> references,
            CancellationToken cancellationToken)
        {
            LookupCallCount++;
            LastReferences = references.ToArray();
            return Task.FromResult(candidates);
        }

        public Task<PaymentEntity?> GetByIdAsync(Guid id, CancellationToken ct)
            => Task.FromResult<PaymentEntity?>(null);

        public Task<PaymentEntity> AddAsync(PaymentEntity entity, CancellationToken ct)
            => Task.FromResult(entity);

        public void Update(PaymentEntity entity) { }
        public void Remove(PaymentEntity entity) { }
        public IQueryable<PaymentEntity> Query() => Array.Empty<PaymentEntity>().AsQueryable();
        public IQueryable<PaymentEntity> QueryNoTracking() => Query();

        public Task<PaymentEntity?> FindByIdempotencyKeyAsync(string idempotencyKey, CancellationToken cancellationToken)
            => Task.FromResult<PaymentEntity?>(null);

        public Task<PaymentEntity?> FindByReferenceAsync(
            PaymentReferenceType referenceType,
            Guid referenceId,
            CancellationToken cancellationToken)
            => Task.FromResult<PaymentEntity?>(null);

        public Task AcquirePaymentReferenceLockAsync(
            PaymentReferenceType referenceType,
            Guid referenceId,
            CancellationToken cancellationToken)
            => Task.CompletedTask;

        public Task<WalletTransaction> DebitWalletBookingPaymentAsync(
            Guid userId,
            Guid bookingId,
            Money amount,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<WalletTransaction> DebitWalletPaymentAsync(
            Guid userId,
            Guid referenceId,
            Money amount,
            WalletTransactionRef walletRef,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<IReadOnlyList<PaymentEntity>> ExpirePendingRedirectDueAsync(
            DateTimeOffset legacyCreatedAtOrBefore,
            DateTimeOffset expiredAt,
            CancellationToken cancellationToken)
            => Task.FromResult<IReadOnlyList<PaymentEntity>>([]);

        public Task<bool> TryMarkRefundedByReferenceAsync(
            PaymentReferenceType referenceType,
            Guid referenceId,
            DateTimeOffset refundedAt,
            CancellationToken cancellationToken)
            => Task.FromResult(false);
    }
}
