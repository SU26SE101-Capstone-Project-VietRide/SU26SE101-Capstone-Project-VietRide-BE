using System.Reflection;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Parcel.Application.Features.Parcels.ConfirmPaymentForParcel;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.UnitTests.Features;

public sealed class ParcelDepositCallbackRaceTests
{
    private static readonly Guid ParcelId = Guid.NewGuid();
    private static readonly Guid PaymentId = Guid.NewGuid();
    private static readonly Guid SenderUserId = Guid.NewGuid();
    private static readonly Guid OperatorId = Guid.NewGuid();
    private static readonly Guid TripId = Guid.NewGuid();
    private static readonly Guid RouteId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 7, 27, 1, 5, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset DueAt = Now.AddMinutes(-1);

    [Fact]
    public async Task OnTimeDepositCallbackAfterExpiry_RestoresReservation()
    {
        var parcel = CreateParcel(ParcelStatus.EXPIRED);
        var fixture = CreateFixture(parcel);
        fixture.Repository.TryMarkDepositSucceededAsync(
                ParcelId,
                2_000,
                Now,
                Arg.Any<CancellationToken>())
            .Returns((ParcelPaymentTransitionSnapshot?)null);
        fixture.Trip.GetTripParcelSnapshotAsync(TripId, Arg.Any<CancellationToken>())
            .Returns(new TripSnapshotOutcome(
                TripSnapshotOutcomeKind.Success,
                TripSnapshot("SCHEDULED"),
                null));
        fixture.IdempotentTrip.ReserveCargoAsync(
                TripId,
                ParcelId,
                Arg.Any<decimal>(),
                Arg.Any<decimal>(),
                PaymentId,
                Arg.Any<CancellationToken>())
            .Returns(new TripCargoOutcome(TripCargoOutcomeKind.Success, null));
        fixture.Repository.TryReconcileExpiredDepositAsync(
                ParcelId,
                PaymentId,
                2_000,
                true,
                Money.Zero,
                "PAYMENT_CALLBACK_DELAY_CANNOT_SERVE",
                Now,
                Arg.Any<CancellationToken>())
            .Returns(Snapshot(ParcelStatus.RESERVED));

        var handled = await fixture.Handler.Handle(
            DepositSucceeded(DueAt.AddSeconds(-1)),
            CancellationToken.None);

        handled.Should().BeTrue();
        await fixture.Repository.Received(1).TryReconcileExpiredDepositAsync(
            ParcelId,
            PaymentId,
            2_000,
            true,
            Money.Zero,
            "PAYMENT_CALLBACK_DELAY_CANNOT_SERVE",
            Now,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task DepositPaidAtDeadline_ExpiresParcelAndReleasesHold()
    {
        var parcel = CreateParcel(ParcelStatus.PENDING_PAYMENT);
        var fixture = CreateFixture(parcel);
        fixture.Repository.TryMarkDepositExpiredAsync(
                ParcelId,
                Now,
                Arg.Any<CancellationToken>())
            .Returns(Snapshot(ParcelStatus.EXPIRED));
        fixture.IdempotentTrip.ReleaseCargoAsync(
                TripId,
                ParcelId,
                Arg.Any<decimal>(),
                Arg.Any<decimal>(),
                PaymentId,
                Arg.Any<CancellationToken>())
            .Returns(new TripCargoOutcome(TripCargoOutcomeKind.Success, null));

        var handled = await fixture.Handler.Handle(
            DepositSucceeded(DueAt),
            CancellationToken.None);

        handled.Should().BeTrue();
        await fixture.Repository.DidNotReceive().TryMarkDepositSucceededAsync(
            Arg.Any<Guid>(),
            Arg.Any<long>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
        await fixture.IdempotentTrip.Received(1).ReleaseCargoAsync(
            TripId,
            ParcelId,
            Arg.Any<decimal>(),
            Arg.Any<decimal>(),
            PaymentId,
            Arg.Any<CancellationToken>());
    }

    private static Fixture CreateFixture(ParcelEntity parcel)
    {
        var repository = Substitute.For<IParcelRepository>();
        repository.GetByIdAsync(ParcelId, Arg.Any<CancellationToken>()).Returns(parcel);
        var trip = Substitute.For<ITripServiceClient, IIdempotentTripServiceClient>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        var handler = new ConfirmPaymentForParcelCommandHandler(
            repository,
            Substitute.For<IIntegrationEventOutbox>(),
            Substitute.For<IParcelStatsRepository>(),
            trip,
            clock,
            Substitute.For<ILogger<ConfirmPaymentForParcelCommandHandler>>());
        return new Fixture(
            handler,
            repository,
            trip,
            (IIdempotentTripServiceClient)trip);
    }

    private static ParcelEntity CreateParcel(ParcelStatus status)
    {
        var parcel = ParcelEntity.CreatePendingPayment(
            "VRP-20260727-DEPOSIT1",
            SenderUserId,
            null,
            "Receiver",
            PhoneNumber.Normalize("+84912345678"),
            null,
            OperatorId,
            TripId,
            null,
            null,
            null,
            null,
            ParcelSizeCategory.MEDIUM,
            estimatedLengthCm: 10m,
            estimatedWidthCm: 10m,
            estimatedHeightCm: 10m,
            estimatedWeightKg: 10m,
            estimatedVolumeM3: 0.001m,
            estimatedDimWeightKg: 0.17m,
            estimatedChargeableWeightKg: 10m,
            ParcelDeliveryMethod.TERMINAL_PICKUP,
            totalPrice: Money.FromRaw(10_000),
            depositPercent: 20m,
            depositAmount: Money.FromRaw(2_000));
        SetPrivateProperty(parcel, nameof(ParcelEntity.Id), ParcelId);
        parcel.ConfigureSettlementV2(
            ParcelSizeCategory.MEDIUM,
            Money.FromRaw(10_000),
            Money.Zero,
            Money.FromRaw(10_000),
            20m,
            Money.FromRaw(2_000),
            Money.FromRaw(1_000),
            Money.Zero,
            ParcelCargoCalculator.DefaultDimWeightFactor,
            Now.AddHours(1),
            Now.AddMinutes(30));
        SetPrivateProperty(parcel, nameof(ParcelEntity.Status), status);
        SetPrivateProperty(parcel, nameof(ParcelEntity.DepositPaymentId), (Guid?)PaymentId);
        return parcel;
    }

    private static ConfirmPaymentForParcelCommand DepositSucceeded(DateTimeOffset paidAt)
        => new(
            PaymentId,
            "PARCEL",
            ParcelId,
            2_000,
            "VNPAY",
            paidAt,
            DueAt);

    private static ParcelPaymentTransitionSnapshot Snapshot(ParcelStatus status)
        => new(
            ParcelId,
            "VRP-20260727-DEPOSIT1",
            status,
            DepositAmount: 2_000,
            AdditionalAmount: 0,
            OperatorId,
            TripId,
            BookingId: null,
            SenderUserId,
            ParcelSizeCategory.MEDIUM,
            AdditionalPaymentId: null);

    private static TripParcelSnapshot TripSnapshot(string status)
        => new(
            TripId,
            OperatorId,
            RouteId,
            Guid.NewGuid(),
            status,
            Now.AddHours(1),
            Now.AddHours(5),
            10_000,
            new TripStationDto(Guid.NewGuid(), "Origin"),
            new TripStationDto(Guid.NewGuid(), "Destination"),
            [],
            new TripSeatSummaryDto(20, 20),
            null);

    private static void SetPrivateProperty<T>(ParcelEntity parcel, string propertyName, T value)
        => typeof(ParcelEntity)
            .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)!
            .SetValue(parcel, value);

    private sealed record Fixture(
        ConfirmPaymentForParcelCommandHandler Handler,
        IParcelRepository Repository,
        ITripServiceClient Trip,
        IIdempotentTripServiceClient IdempotentTrip);
}
