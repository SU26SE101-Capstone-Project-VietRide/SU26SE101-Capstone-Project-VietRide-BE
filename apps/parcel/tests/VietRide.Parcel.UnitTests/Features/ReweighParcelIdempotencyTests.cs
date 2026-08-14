using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Parcel.Application.Features.Parcels.Reweigh;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.UnitTests.Features;

public sealed class ReweighParcelIdempotencyTests
{
    private static readonly Guid ParcelId = Guid.NewGuid();
    private static readonly Guid SenderUserId = Guid.NewGuid();
    private static readonly Guid AssistantUserId = Guid.NewGuid();
    private static readonly Guid OperatorId = Guid.NewGuid();
    private static readonly Guid TripId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 7, 27, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Handle_PositiveBalance_UsesFareSnapshotAndMovesToPendingFinalPayment()
    {
        var operationId = Guid.NewGuid();
        var fixture = CreateFixture(
            depositPaidVnd: 400,
            loadCutoffAt: Now.AddMinutes(20),
            cargoOutcome: SuccessCargo());
        SetupSettlementResult(fixture.ParcelRepository, ParcelStatus.PENDING_FINAL_PAYMENT);

        var result = await fixture.Handler.Handle(
            CreateCommand(operationId, actualWeightKg: 3.2m),
            CancellationToken.None);

        result.Status.Should().Be(nameof(ParcelStatus.PENDING_FINAL_PAYMENT));
        result.ActualSizeCategory.Should().Be(nameof(ParcelSizeCategory.SMALL));
        result.FinalGrossPriceVnd.Should().Be(3_200);
        result.FinalTotalPriceVnd.Should().Be(3_200);
        result.DepositPaidVnd.Should().Be(400);
        result.BalanceRequiredVnd.Should().Be(2_800);
        result.RefundDueVnd.Should().Be(0);
        result.FinalPaymentDeadline.Should().Be(Now.AddMinutes(20));
        fixture.Outbox.Events.Should().ContainSingle();
        var integrationEvent = fixture.Outbox.Events.Single();
        integrationEvent.EventType.Should().Be(ParcelOutboxEvents.FinalPaymentRequested);
        using var payload = JsonDocument.Parse(integrationEvent.PayloadJson);
        payload.RootElement.GetProperty("eventId").GetGuid().Should().Be(integrationEvent.EventId);
        payload.RootElement.GetProperty("parcelId").GetGuid().Should().Be(ParcelId);
        payload.RootElement.GetProperty("parcelCode").GetString().Should().Be("VRP-20260727-TEST0001");
        payload.RootElement.GetProperty("operatorId").GetGuid().Should().Be(OperatorId);
        payload.RootElement.GetProperty("userId").GetGuid().Should().Be(SenderUserId);
        payload.RootElement.GetProperty("tripId").GetGuid().Should().Be(TripId);
        payload.RootElement.GetProperty("balanceRequiredVnd").GetInt64().Should().Be(2_800);
        payload.RootElement.GetProperty("balancePaidVnd").GetInt64().Should().Be(0);
        payload.RootElement.GetProperty("finalPaymentDeadline").GetDateTimeOffset()
            .Should().Be(Now.AddMinutes(20));
        await fixture.IdempotentTrip.Received(1).RemeasureCargoAsync(
            TripId,
            ParcelId,
            3.2m,
            Arg.Any<decimal>(),
            false,
            operationId,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_LowerFinalPrice_MovesToReadyAndEnqueuesIdempotentRefund()
    {
        var fixture = CreateFixture(
            depositPaidVnd: 2_000,
            loadCutoffAt: Now.AddHours(1),
            cargoOutcome: SuccessCargo());
        SetupSettlementResult(fixture.ParcelRepository, ParcelStatus.READY_TO_LOAD);

        var result = await fixture.Handler.Handle(
            CreateCommand(Guid.NewGuid(), actualWeightKg: 1m),
            CancellationToken.None);

        result.Status.Should().Be(nameof(ParcelStatus.READY_TO_LOAD));
        result.FinalTotalPriceVnd.Should().Be(1_000);
        result.BalanceRequiredVnd.Should().Be(0);
        result.RefundDueVnd.Should().Be(1_000);
        result.FinalPaymentDeadline.Should().BeNull();
        fixture.Outbox.Events.Should().ContainSingle();
        fixture.Outbox.Events.Single().EventType.Should().Be(ParcelOutboxEvents.RefundInitiated);
        using var payload = JsonDocument.Parse(fixture.Outbox.Events.Single().PayloadJson);
        payload.RootElement.GetProperty("idempotencyKey").GetString()
            .Should().Be($"{ParcelId:D}:SETTLEMENT_PRICE_DECREASE");
    }

    [Fact]
    public async Task Handle_CapacityExceeded_PersistsSettlementAndPendingResumeStatus()
    {
        var fixture = CreateFixture(
            depositPaidVnd: 400,
            loadCutoffAt: Now.AddHours(1),
            cargoOutcome: new TripCargoOutcome(
                TripCargoOutcomeKind.CapacityExceeded,
                "Actual cargo exceeds capacity."));
        SetupSettlementResult(fixture.ParcelRepository, ParcelStatus.PENDING_OPERATOR_ACTION);

        var result = await fixture.Handler.Handle(
            CreateCommand(Guid.NewGuid(), actualWeightKg: 4m),
            CancellationToken.None);

        result.Status.Should().Be(nameof(ParcelStatus.PENDING_OPERATOR_ACTION));
        await fixture.ParcelRepository.Received(1).TrySettleReweighAsync(
            ParcelId,
            AssistantUserId,
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
            Arg.Any<Money>(),
            Arg.Any<Money>(),
            Arg.Any<DateTimeOffset?>(),
            ParcelStatus.PENDING_FINAL_PAYMENT,
            false,
            "Actual cargo exceeds capacity.",
            Now,
            Arg.Any<CancellationToken>());
        fixture.Outbox.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_InvalidTripCargoState_DoesNotPersistFalseCapacityAction()
    {
        var fixture = CreateFixture(
            depositPaidVnd: 400,
            loadCutoffAt: Now.AddHours(1),
            cargoOutcome: new TripCargoOutcome(
                TripCargoOutcomeKind.InvalidState,
                "Only reserved cargo can be remeasured."));

        var action = () => fixture.Handler.Handle(
            CreateCommand(Guid.NewGuid(), actualWeightKg: 30m),
            CancellationToken.None);

        var exception = await action.Should().ThrowAsync<CodedConflictException>();
        exception.Which.ErrorCode.Should().Be("TRIP_CARGO_STATE_INVALID");
        exception.Which.Message.Should().Be("Only reserved cargo can be remeasured.");
        await fixture.ParcelRepository.DidNotReceiveWithAnyArgs().TrySettleReweighAsync(
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default,
            default);
        fixture.Outbox.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task Handle_DerivesLargeSizeFromActualChargeableWeight()
    {
        var fixture = CreateFixture(
            depositPaidVnd: 2_000,
            loadCutoffAt: Now.AddHours(1),
            cargoOutcome: SuccessCargo());
        SetupSettlementResult(fixture.ParcelRepository, ParcelStatus.PENDING_FINAL_PAYMENT);

        var result = await fixture.Handler.Handle(
            CreateCommand(Guid.NewGuid(), actualWeightKg: 16m),
            CancellationToken.None);

        result.ActualSizeCategory.Should().Be(nameof(ParcelSizeCategory.LARGE));
        result.FinalGrossPriceVnd.Should().Be(16_000);
    }

    private static Fixture CreateFixture(
        long depositPaidVnd,
        DateTimeOffset loadCutoffAt,
        TripCargoOutcome cargoOutcome)
    {
        var parcel = CreateCheckedInParcel(depositPaidVnd, loadCutoffAt);
        var parcelRepository = Substitute.For<IParcelRepository>();
        parcelRepository.GetByIdAsync(ParcelId, Arg.Any<CancellationToken>()).Returns(parcel);

        var trip = Substitute.For<ITripServiceClient, IIdempotentTripServiceClient>();
        trip.AuthorizeAssistantForTripAsync(
                TripId,
                AssistantUserId,
                OperatorId,
                Arg.Any<CancellationToken>())
            .Returns(new TripCrewAuthorizationOutcome(TripCrewAuthorizationOutcomeKind.Authorized));
        var idempotentTrip = (IIdempotentTripServiceClient)trip;
        idempotentTrip.RemeasureCargoAsync(
                TripId,
                ParcelId,
                Arg.Any<decimal>(),
                Arg.Any<decimal>(),
                Arg.Any<bool>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>())
            .Returns(cargoOutcome);

        var outbox = new RecordingOutbox();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);

        return new Fixture(
            new ReweighParcelCommandHandler(parcelRepository, trip, outbox, unitOfWork, clock),
            parcelRepository,
            idempotentTrip,
            outbox);
    }

    private static ParcelEntity CreateCheckedInParcel(long depositPaidVnd, DateTimeOffset loadCutoffAt)
    {
        var parcel = ParcelEntity.CreatePendingPayment(
            "VRP-20260727-TEST0001",
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
            ParcelSizeCategory.SMALL,
            estimatedLengthCm: 10m,
            estimatedWidthCm: 10m,
            estimatedHeightCm: 10m,
            estimatedWeightKg: 2m,
            estimatedVolumeM3: 0.001m,
            estimatedDimWeightKg: 0.17m,
            estimatedChargeableWeightKg: 2m,
            ParcelDeliveryMethod.TERMINAL_PICKUP,
            totalPrice: Money.FromRaw(2_000),
            depositPercent: 20m,
            depositAmount: Money.FromRaw(400));
        SetPrivateProperty(parcel, nameof(ParcelEntity.Id), ParcelId);
        parcel.ConfigureSettlementV2(
            ParcelSizeCategory.SMALL,
            Money.FromRaw(2_000),
            Money.Zero,
            Money.FromRaw(2_000),
            20m,
            Money.FromRaw(400),
            Money.FromRaw(1_000),
            Money.Zero,
            ParcelCargoCalculator.DefaultDimWeightFactor,
            loadCutoffAt,
            loadCutoffAt.AddMinutes(-10));
        SetPrivateProperty(parcel, nameof(ParcelEntity.Status), ParcelStatus.CHECKED_IN);
        SetPrivateProperty(parcel, nameof(ParcelEntity.DepositPaidVnd), Money.FromRaw(depositPaidVnd));
        return parcel;
    }

    private static void SetupSettlementResult(
        IParcelRepository repository,
        ParcelStatus status)
    {
        repository.TrySettleReweighAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
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
                Arg.Any<Money>(),
                Arg.Any<Money>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<ParcelStatus>(),
                Arg.Any<bool>(),
                Arg.Any<string?>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(new ParcelPaymentTransitionSnapshot(
                ParcelId,
                "VRP-20260727-TEST0001",
                status,
                DepositAmount: 400,
                AdditionalAmount: 0,
                OperatorId,
                TripId,
                BookingId: null,
                SenderUserId,
                ParcelSizeCategory.SMALL,
                AdditionalPaymentId: null));
    }

    private static ReweighParcelCommand CreateCommand(Guid operationId, decimal actualWeightKg)
        => new(
            ParcelId,
            OperatorId,
            AssistantUserId,
            ActualLengthCm: 10m,
            ActualWidthCm: 10m,
            ActualHeightCm: 10m,
            actualWeightKg,
            operationId.ToString("D"));

    private static TripCargoOutcome SuccessCargo()
        => new(TripCargoOutcomeKind.Success, null);

    private static void SetPrivateProperty<T>(ParcelEntity parcel, string propertyName, T value)
        => typeof(ParcelEntity)
            .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)!
            .SetValue(parcel, value);

    private sealed record Fixture(
        ReweighParcelCommandHandler Handler,
        IParcelRepository ParcelRepository,
        IIdempotentTripServiceClient IdempotentTrip,
        RecordingOutbox Outbox);

    private sealed class RecordingOutbox : IIntegrationEventOutbox
    {
        public List<(Guid EventId, string EventType, string PayloadJson)> Events { get; } = [];

        public Task EnqueueAsync(
            Guid eventId,
            string eventType,
            string payloadJson,
            CancellationToken ct = default)
        {
            Events.Add((eventId, eventType, payloadJson));
            return Task.CompletedTask;
        }

        public Task EnqueueAsync(string eventType, string payloadJson, CancellationToken ct = default)
        {
            Events.Add((Guid.NewGuid(), eventType, payloadJson));
            return Task.CompletedTask;
        }
    }
}
