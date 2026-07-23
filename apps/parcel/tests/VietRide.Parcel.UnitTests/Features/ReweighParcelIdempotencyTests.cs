using System.Reflection;
using FluentAssertions;
using NSubstitute;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Features.Parcels.Reweigh;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.ValueObjects;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.UnitTests.Features;

public sealed class ReweighParcelIdempotencyTests
{
    private static readonly Guid ParcelId = Guid.NewGuid();
    private static readonly Guid SenderUserId = Guid.NewGuid();
    private static readonly Guid OperatorId = Guid.NewGuid();
    private static readonly Guid TripId = Guid.NewGuid();
    private static readonly Guid RouteId = Guid.NewGuid();
    private static readonly DateTimeOffset Departure = DateTimeOffset.UtcNow.AddHours(6);

    [Fact]
    public async Task Handle_NoFee_RemeasuresTripOnceWithParentRequestKey()
    {
        var operationId = Guid.NewGuid();
        var fixture = CreateFixture(estimatedWeightKg: 1m, paidAmount: 100_000);
        fixture.ParcelRepository.TryReweighNoFeeAsync(
                ParcelId,
                Arg.Any<decimal>(),
                Arg.Any<decimal>(),
                Arg.Any<decimal>(),
                Arg.Any<decimal>(),
                Arg.Any<decimal>(),
                Arg.Any<decimal>(),
                Arg.Any<decimal>(),
                Arg.Any<ParcelSizeCategory>(),
                Arg.Any<Money>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(CreateTransition(ParcelStatus.PENDING, additionalAmount: 0));

        var result = await fixture.Handler.Handle(
            CreateCommand(operationId, actualWeightKg: 1m),
            CancellationToken.None);

        result.Status.Should().Be(ParcelStatus.PENDING.ToString());
        await fixture.IdempotentTrip.Received(1).RemeasureCargoAsync(
            TripId,
            ParcelId,
            1m,
            Arg.Any<decimal>(),
            true,
            operationId,
            Arg.Any<CancellationToken>());
        await fixture.Payment.DidNotReceive().ChargeParcelPaymentAsync(
            Arg.Any<string>(),
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<long>(),
            Arg.Any<string>(),
            Arg.Any<string>(),
            Arg.Any<CancellationToken>(),
            Arg.Any<PaymentContextSnapshot?>());
    }

    [Fact]
    public async Task Handle_AdditionalFee_UsesParentRequestKeyForTripAndPayment()
    {
        var operationId = Guid.NewGuid();
        var fixture = CreateFixture(estimatedWeightKg: 1m, paidAmount: 100_000);
        fixture.ParcelRepository.TryReweighWithFeeAsync(
                ParcelId,
                Arg.Any<decimal>(),
                Arg.Any<decimal>(),
                Arg.Any<decimal>(),
                Arg.Any<decimal>(),
                Arg.Any<decimal>(),
                Arg.Any<decimal>(),
                Arg.Any<decimal>(),
                Arg.Any<ParcelSizeCategory>(),
                Arg.Any<Money>(),
                Arg.Any<Money>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(CreateTransition(ParcelStatus.PENDING_ADDITIONAL_PAYMENT, additionalAmount: 100_000));
        fixture.Identity.GetOperatorInfoAsync(OperatorId, Arg.Any<CancellationToken>())
            .Returns(new OperatorLookupOutcome(
                OperatorLookupOutcomeKind.Success,
                new IdentityOperatorInfo(OperatorId, "Operator", ParcelNoShowPolicy.Default),
                null));
        var paymentId = Guid.NewGuid();
        fixture.Payment.ChargeParcelPaymentAsync(
                "PARCEL_ADDITIONAL",
                ParcelId,
                SenderUserId,
                100_000,
                "WALLET",
                operationId.ToString("D"),
                Arg.Any<CancellationToken>(),
                Arg.Any<PaymentContextSnapshot?>())
            .Returns(new ChargeOutcome(
                ChargeOutcomeKind.Success,
                new ChargeResult(paymentId, "PENDING", null),
                null));
        fixture.ParcelRepository.TryAssignAdditionalPaymentIdAsync(
                ParcelId,
                paymentId,
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(true);

        var result = await fixture.Handler.Handle(
            CreateCommand(operationId, actualWeightKg: 2m),
            CancellationToken.None);

        result.Status.Should().Be(ParcelStatus.PENDING_ADDITIONAL_PAYMENT.ToString());
        result.AdditionalAmount.Should().Be(100_000);
        await fixture.IdempotentTrip.Received(1).RemeasureCargoAsync(
            TripId,
            ParcelId,
            2m,
            Arg.Any<decimal>(),
            true,
            operationId,
            Arg.Any<CancellationToken>());
        await fixture.Payment.Received(1).ChargeParcelPaymentAsync(
            "PARCEL_ADDITIONAL",
            ParcelId,
            SenderUserId,
            100_000,
            "WALLET",
            operationId.ToString("D"),
            Arg.Any<CancellationToken>(),
            Arg.Is<PaymentContextSnapshot?>(context =>
                context != null
                && context.Allocations.Count == 1
                && context.Allocations[0].ReferenceId == ParcelId));
    }

    private static Fixture CreateFixture(decimal estimatedWeightKg, long paidAmount)
    {
        var parcel = ParcelEntity.CreatePendingPayment(
            "VRP-20260723-TEST0001",
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
            estimatedWeightKg,
            estimatedVolumeM3: 0.001m,
            estimatedDimWeightKg: 0.2m,
            estimatedChargeableWeightKg: estimatedWeightKg,
            ParcelDeliveryMethod.TERMINAL_PICKUP,
            Money.FromRaw(paidAmount),
            depositPercent: 100m,
            Money.FromRaw(paidAmount));
        SetPrivateProperty(parcel, nameof(ParcelEntity.Id), ParcelId);
        SetPrivateProperty(parcel, nameof(ParcelEntity.Status), ParcelStatus.PENDING);

        var parcelRepository = Substitute.For<IParcelRepository>();
        parcelRepository.GetByIdAsync(ParcelId, Arg.Any<CancellationToken>()).Returns(parcel);

        var fareRepository = Substitute.For<IParcelRouteFareRepository>();
        var fare = ParcelRouteFare.Create(
            RouteId,
            ParcelSizeCategory.MEDIUM,
            OperatorId,
            Money.FromRaw(100_000),
            DateTimeOffset.UtcNow.AddDays(-1));
        fare.UpdateWeightPricing(Money.FromRaw(100_000), Money.Zero);
        fareRepository.FindByCompositeAsync(
                RouteId,
                ParcelSizeCategory.MEDIUM,
                Arg.Any<CancellationToken>())
            .Returns(fare);

        var policyRepository = Substitute.For<IParcelPricingPolicyRepository>();
        policyRepository.GetSystemDecimalAsync(
                Arg.Any<string>(),
                Arg.Any<decimal>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(call => call.ArgAt<decimal>(1));

        var trip = Substitute.For<ITripServiceClient, IIdempotentTripServiceClient>();
        trip.GetTripParcelSnapshotAsync(TripId, Arg.Any<CancellationToken>())
            .Returns(new TripSnapshotOutcome(
                TripSnapshotOutcomeKind.Success,
                CreateTripSnapshot(),
                null));
        var capacity = new TripCargoCapacitySnapshot(
            TripId,
            ReservedWeightKg: estimatedWeightKg,
            ReservedVolumeM3: 0.001m,
            LoadedWeightKg: 0m,
            LoadedVolumeM3: 0m,
            MaxCargoWeightKg: 100m,
            MaxCargoVolumeM3: 10m,
            AvailableWeightKg: 100m - estimatedWeightKg,
            AvailableVolumeM3: 9.999m,
            PercentFull: estimatedWeightKg);
        trip.GetCargoCapacityAsync(TripId, Arg.Any<CancellationToken>())
            .Returns(new TripCargoOutcome(TripCargoOutcomeKind.Success, null, capacity));
        var idempotentTrip = (IIdempotentTripServiceClient)trip;
        idempotentTrip.RemeasureCargoAsync(
                TripId,
                ParcelId,
                Arg.Any<decimal>(),
                Arg.Any<decimal>(),
                Arg.Any<bool>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>())
            .Returns(new TripCargoOutcome(TripCargoOutcomeKind.Success, null, capacity));

        var identity = Substitute.For<IIdentityServiceClient>();
        var payment = Substitute.For<IPaymentServiceClient>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);

        var handler = new ReweighParcelCommandHandler(
            parcelRepository,
            fareRepository,
            policyRepository,
            trip,
            identity,
            payment,
            unitOfWork);

        return new Fixture(
            handler,
            parcelRepository,
            idempotentTrip,
            identity,
            payment);
    }

    private static ReweighParcelCommand CreateCommand(Guid operationId, decimal actualWeightKg)
        => new(
            ParcelId,
            OperatorId,
            ActualLengthCm: 10m,
            ActualWidthCm: 10m,
            ActualHeightCm: 10m,
            actualWeightKg,
            ActualSizeCategory: "MEDIUM",
            PaymentMethod: "WALLET",
            IdempotencyKey: operationId.ToString("D"));

    private static ParcelPaymentTransitionSnapshot CreateTransition(
        ParcelStatus status,
        long additionalAmount)
        => new(
            ParcelId,
            "VRP-20260723-TEST0001",
            status,
            DepositAmount: 100_000,
            additionalAmount,
            OperatorId,
            TripId,
            BookingId: null,
            SenderUserId,
            ParcelSizeCategory.MEDIUM,
            AdditionalPaymentId: null);

    private static TripParcelSnapshot CreateTripSnapshot()
        => new(
            TripId,
            OperatorId,
            RouteId,
            Guid.NewGuid(),
            "SCHEDULED",
            Departure,
            Departure.AddHours(4),
            100_000,
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
        ReweighParcelCommandHandler Handler,
        IParcelRepository ParcelRepository,
        IIdempotentTripServiceClient IdempotentTrip,
        IIdentityServiceClient Identity,
        IPaymentServiceClient Payment);
}
