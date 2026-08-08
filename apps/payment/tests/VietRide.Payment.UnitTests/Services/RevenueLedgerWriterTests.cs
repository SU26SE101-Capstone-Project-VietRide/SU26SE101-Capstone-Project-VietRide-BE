using FluentAssertions;
using VietRide.Payment.Application.Abstractions.Repositories;
using VietRide.Payment.Application.Models;
using VietRide.Payment.Application.Services;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;

namespace VietRide.Payment.UnitTests.Services;

public sealed class RevenueLedgerWriterTests
{
    [Fact]
    public async Task RecordPaymentSucceededAsync_WritesPaidRevenueAndVoucherFundingEntries()
    {
        var sourceEventId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var bookingId = Guid.NewGuid();
        var repository = new FakeLedgerRepository();
        var writer = new RevenueLedgerWriter(repository);
        var context = new PaymentContextV1(1,
        [
            new PaymentAllocationV1(
                bookingId,
                "BOOKING",
                operatorId,
                tripId,
                200_000,
                30_000,
                20_000),
        ]);

        await writer.RecordPaymentSucceededAsync(
            sourceEventId,
            context,
            CancellationToken.None);

        repository.Entries.Should().HaveCount(3);
        repository.Entries.Should().ContainSingle(entry =>
            entry.EntryType == OperatorLedgerEntryType.BOOKING_REVENUE
            && entry.Amount == 150_000);
        repository.Entries.Should().ContainSingle(entry =>
            entry.EntryType == OperatorLedgerEntryType.VOUCHER_VIETRIDE_FUNDED_CREDIT
            && entry.Amount == 30_000);
        repository.Entries.Should().ContainSingle(entry =>
            entry.EntryType == OperatorLedgerEntryType.VOUCHER_OPERATOR_FUNDED_AUDIT
            && entry.Amount == 0
            && entry.Note == "operator-funded-voucher:20000");
        repository.Entries.Should().OnlyContain(entry =>
            entry.SourceEventId == sourceEventId
            && entry.ReferenceId == bookingId
            && entry.OperatorId == operatorId
            && entry.TripId == tripId);
    }

    [Fact]
    public async Task RecordRefundAsync_PartialBookingRefund_ReversesFullVietRideVoucherCredit()
    {
        var sourceEventId = Guid.NewGuid();
        var bookingId = Guid.NewGuid();
        var repository = new FakeLedgerRepository();
        var writer = new RevenueLedgerWriter(repository);
        var context = new PaymentContextV1(1,
        [
            new PaymentAllocationV1(
                bookingId,
                "BOOKING",
                Guid.NewGuid(),
                Guid.NewGuid(),
                200_000,
                30_000,
                20_000),
        ]);

        await writer.RecordRefundAsync(
            sourceEventId,
            context,
            bookingId,
            75_000,
            CancellationToken.None);

        repository.Entries.Should().HaveCount(2);
        repository.Entries.Should().ContainSingle(entry =>
            entry.EntryType == OperatorLedgerEntryType.BOOKING_REFUND
            && entry.Amount == -75_000);
        repository.Entries.Should().ContainSingle(entry =>
            entry.EntryType == OperatorLedgerEntryType.ADJUSTMENT
            && entry.Amount == -30_000
            && entry.AdjustmentReason == OperatorLedgerAdjustmentReason.VIETRIDE_FUNDED_VOUCHER_REVERSAL
            && entry.Note == "reverse-vietride-funded-voucher");
        repository.Entries.Should().OnlyContain(entry =>
            entry.SourceEventId == sourceEventId
            && entry.ReferenceId == bookingId);
    }

    [Fact]
    public async Task RecordRefundAsync_ParcelRefund_UsesTypedDeterministicVoucherReversal()
    {
        var sourceEventId = Guid.NewGuid();
        var parcelId = Guid.NewGuid();
        var repository = new FakeLedgerRepository();
        var writer = new RevenueLedgerWriter(repository);
        var context = new PaymentContextV1(1,
        [
            new PaymentAllocationV1(
                parcelId,
                "PARCEL",
                Guid.NewGuid(),
                Guid.NewGuid(),
                200_000,
                30_000,
                0),
        ]);

        await writer.RecordRefundAsync(
            sourceEventId,
            context,
            parcelId,
            75_000,
            CancellationToken.None);

        repository.Entries.Should().ContainSingle(entry =>
            entry.EntryType == OperatorLedgerEntryType.PARCEL_REFUND
            && entry.Amount == -75_000
            && entry.SourceEventId == sourceEventId);
        var adjustment = repository.Entries.Should().ContainSingle(entry =>
            entry.EntryType == OperatorLedgerEntryType.ADJUSTMENT).Which;
        adjustment.Amount.Should().Be(-30_000);
        adjustment.AdjustmentReason.Should().Be(
            OperatorLedgerAdjustmentReason.VIETRIDE_FUNDED_VOUCHER_REVERSAL);
        adjustment.SourceEventId.Should().Be(
            RevenueLedgerWriter.CreateParcelVoucherAdjustmentSourceId(sourceEventId, parcelId));
        adjustment.SourceEventId.Should().NotBe(sourceEventId);
    }

    [Fact]
    public async Task RecordGenericBookingRefundEntitlementAsync_UsesTypedZeroMarker()
    {
        var bookingId = Guid.NewGuid();
        var repository = new FakeLedgerRepository();
        var writer = new RevenueLedgerWriter(repository);
        var context = new PaymentContextV1(1,
        [
            new PaymentAllocationV1(
                bookingId,
                "BOOKING",
                Guid.NewGuid(),
                Guid.NewGuid(),
                200_000,
                0,
                0),
        ]);

        await writer.RecordGenericBookingRefundEntitlementAsync(
            Guid.NewGuid(),
            context,
            bookingId,
            CancellationToken.None);

        repository.Entries.Should().ContainSingle().Which.AdjustmentReason.Should().Be(
            OperatorLedgerAdjustmentReason.GENERIC_BOOKING_REFUND_ENTITLEMENT);
    }

    [Fact]
    public async Task RecordRefundAsync_OperatorFundedVoucher_DoesNotWriteMonetaryAdjustment()
    {
        var bookingId = Guid.NewGuid();
        var repository = new FakeLedgerRepository();
        var writer = new RevenueLedgerWriter(repository);
        var context = new PaymentContextV1(1,
        [
            new PaymentAllocationV1(
                bookingId,
                "BOOKING",
                Guid.NewGuid(),
                Guid.NewGuid(),
                200_000,
                0,
                30_000),
        ]);

        await writer.RecordRefundAsync(
            Guid.NewGuid(),
            context,
            bookingId,
            75_000,
            CancellationToken.None);

        repository.Entries.Should().ContainSingle()
            .Which.Should().Match<OperatorLedgerEntry>(entry =>
                entry.EntryType == OperatorLedgerEntryType.BOOKING_REFUND
                && entry.Amount == -75_000);
        repository.Entries.Should().NotContain(entry =>
            entry.EntryType == OperatorLedgerEntryType.ADJUSTMENT);
    }

    [Fact]
    public async Task RecordRefundAsync_NoVoucher_DoesNotWriteAdjustment()
    {
        var bookingId = Guid.NewGuid();
        var repository = new FakeLedgerRepository();
        var writer = new RevenueLedgerWriter(repository);
        var context = new PaymentContextV1(1,
        [
            new PaymentAllocationV1(
                bookingId,
                "BOOKING",
                Guid.NewGuid(),
                Guid.NewGuid(),
                200_000,
                0,
                0),
        ]);

        await writer.RecordRefundAsync(
            Guid.NewGuid(),
            context,
            bookingId,
            75_000,
            CancellationToken.None);

        repository.Entries.Should().ContainSingle()
            .Which.Should().Match<OperatorLedgerEntry>(entry =>
                entry.EntryType == OperatorLedgerEntryType.BOOKING_REFUND
                && entry.Amount == -75_000);
        repository.Entries.Should().NotContain(entry =>
            entry.EntryType == OperatorLedgerEntryType.ADJUSTMENT);
    }

    private sealed class FakeLedgerRepository : IOperatorLedgerEntryRepository
    {
        public List<OperatorLedgerEntry> Entries { get; } = [];

        public Task<OperatorLedgerEntry> AddAsync(
            OperatorLedgerEntry entity,
            CancellationToken cancellationToken = default)
        {
            Entries.Add(entity);
            return Task.FromResult(entity);
        }

        public Task<OperatorLedgerEntry?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
            => Task.FromResult(Entries.SingleOrDefault(entry => entry.Id == id));

        public void Update(OperatorLedgerEntry entity) => throw new NotSupportedException();
        public void Remove(OperatorLedgerEntry entity) => throw new NotSupportedException();
        public IQueryable<OperatorLedgerEntry> Query() => Entries.AsQueryable();
        public IQueryable<OperatorLedgerEntry> QueryNoTracking() => Query();

        public Task<long> SumTripNetAmountAsync(
            Guid operatorId,
            Guid tripId,
            CancellationToken cancellationToken)
            => Task.FromResult(Entries
                .Where(entry => entry.OperatorId == operatorId && entry.TripId == tripId)
                .Sum(entry => entry.Amount));
    }
}
