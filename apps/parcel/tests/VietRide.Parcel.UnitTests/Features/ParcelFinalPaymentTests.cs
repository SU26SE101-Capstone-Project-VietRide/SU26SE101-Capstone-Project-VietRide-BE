using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Parcel.Application.Features.Parcels.ConfirmPaymentForParcel;
using VietRide.Parcel.Application.Features.Parcels.FinalPayment;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.UnitTests.Features;

public sealed class ParcelFinalPaymentTests
{
    private static readonly Guid ParcelId = Guid.NewGuid();
    private static readonly Guid PaymentId = Guid.NewGuid();
    private static readonly Guid SenderUserId = Guid.NewGuid();
    private static readonly Guid OperatorId = Guid.NewGuid();
    private static readonly Guid TripId = Guid.NewGuid();
    private static readonly Guid RouteId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 7, 27, 1, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Deadline = Now.AddMinutes(20);

    [Fact]
    public async Task StartFinalPayment_ChargesServerDerivedBalanceWithParcelDeadline()
    {
        var parcel = CreateParcel(ParcelStatus.PENDING_FINAL_PAYMENT);
        var repository = Substitute.For<IParcelRepository>();
        repository.GetByIdAsync(ParcelId, Arg.Any<CancellationToken>()).Returns(parcel);
        repository.TryAssignBalancePaymentIdAsync(
                ParcelId,
                PaymentId,
                Now,
                Arg.Any<CancellationToken>())
            .Returns(true);
        var payments = Substitute.For<IPaymentServiceClient>();
        payments.ChargeParcelPaymentAsync(
                "PARCEL_ADDITIONAL",
                ParcelId,
                SenderUserId,
                8_000,
                "VNPAY",
                "request-key",
                Arg.Any<CancellationToken>(),
                Arg.Any<PaymentContextSnapshot?>(),
                Deadline)
            .Returns(new ChargeOutcome(
                ChargeOutcomeKind.Success,
                new ChargeResult(PaymentId, "PENDING_REDIRECT", "https://pay", Deadline),
                null));

        var result = await new StartParcelFinalPaymentCommandHandler(
            repository,
            payments,
            Clock()).Handle(
                new StartParcelFinalPaymentCommand(
                    ParcelId,
                    SenderUserId,
                    "VNPAY",
                    "request-key"),
                CancellationToken.None);

        result.BalancePaymentId.Should().Be(PaymentId);
        result.BalanceRequiredVnd.Should().Be(8_000);
        result.FinalPaymentDeadline.Should().Be(Deadline);
    }

    [Fact]
    public async Task BalanceSuccess_PaidBeforeDeadline_MovesToReadyToLoad()
    {
        var parcel = CreateParcel(ParcelStatus.PENDING_FINAL_PAYMENT);
        var fixture = CreateCallbackFixture(parcel);
        fixture.Repository.TryMarkBalanceSucceededAsync(
                ParcelId,
                PaymentId,
                8_000,
                Deadline.AddSeconds(-1),
                Now,
                Arg.Any<CancellationToken>())
            .Returns(Snapshot(ParcelStatus.READY_TO_LOAD));

        var handled = await fixture.Handler.Handle(
            BalanceSucceeded(Deadline.AddSeconds(-1)),
            CancellationToken.None);

        handled.Should().BeTrue();
        await fixture.Repository.Received(1).TryMarkBalanceSucceededAsync(
            ParcelId,
            PaymentId,
            8_000,
            Deadline.AddSeconds(-1),
            Now,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task BalanceSuccess_AtDeadline_DoesNotIncreaseBalancePaid()
    {
        var fixture = CreateCallbackFixture(CreateParcel(ParcelStatus.PENDING_FINAL_PAYMENT));

        var handled = await fixture.Handler.Handle(
            BalanceSucceeded(Deadline),
            CancellationToken.None);

        handled.Should().BeTrue();
        await fixture.Repository.DidNotReceive().TryMarkBalanceSucceededAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<long>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnTimeCallbackAfterTimeout_RestoresCargoAndReadyToLoad()
    {
        var parcel = CreateParcel(
            ParcelStatus.REJECTED,
            rejectionReason: "FINAL_PAYMENT_TIMEOUT");
        var fixture = CreateCallbackFixture(parcel);
        fixture.Repository.TryMarkBalanceSucceededAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<long>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>(),
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
        fixture.Repository.TryReconcileTimedOutBalanceAsync(
                ParcelId,
                PaymentId,
                8_000,
                Deadline.AddSeconds(-1),
                true,
                Money.Zero,
                "PAYMENT_CALLBACK_DELAY_CANNOT_SERVE",
                Now,
                Arg.Any<CancellationToken>())
            .Returns(Snapshot(ParcelStatus.READY_TO_LOAD));

        var handled = await fixture.Handler.Handle(
            BalanceSucceeded(Deadline.AddSeconds(-1)),
            CancellationToken.None);

        handled.Should().BeTrue();
        fixture.Outbox.Events.Should().ContainSingle();
        var integrationEvent = fixture.Outbox.Events.Single();
        integrationEvent.EventType.Should().Be(ParcelOutboxEvents.SettlementRecovered);
        using var payload = JsonDocument.Parse(integrationEvent.PayloadJson);
        payload.RootElement.GetProperty("eventId").GetGuid().Should().Be(integrationEvent.EventId);
        payload.RootElement.GetProperty("parcelId").GetGuid().Should().Be(ParcelId);
        payload.RootElement.GetProperty("parcelCode").GetString().Should().Be("VRP-20260727-FINAL001");
        payload.RootElement.GetProperty("userId").GetGuid().Should().Be(SenderUserId);
        payload.RootElement.GetProperty("tripId").GetGuid().Should().Be(TripId);
        payload.RootElement.GetProperty("recoveredStatus").GetString().Should().Be("READY_TO_LOAD");
        payload.RootElement.GetProperty("refundAmountVnd").GetInt64().Should().Be(0);
        await fixture.IdempotentTrip.Received(1).ReserveCargoAsync(
            TripId,
            ParcelId,
            Arg.Any<decimal>(),
            Arg.Any<decimal>(),
            PaymentId,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OnTimeCallbackAfterTimeout_WhenTripCannotServe_CancelsAndRefundsAllCollected()
    {
        var parcel = CreateParcel(
            ParcelStatus.REJECTED,
            rejectionReason: "FINAL_PAYMENT_TIMEOUT");
        SetPrivateProperty(parcel, nameof(ParcelEntity.LoadCutoffAt), Now);
        var fixture = CreateCallbackFixture(parcel);
        fixture.Repository.TryMarkBalanceSucceededAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<long>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns((ParcelPaymentTransitionSnapshot?)null);
        fixture.Repository.TryReconcileTimedOutBalanceAsync(
                ParcelId,
                PaymentId,
                8_000,
                Deadline.AddSeconds(-1),
                false,
                Money.FromRaw(10_000),
                "PAYMENT_CALLBACK_DELAY_CANNOT_SERVE",
                Now,
                Arg.Any<CancellationToken>())
            .Returns(Snapshot(ParcelStatus.CANCELLED));

        var handled = await fixture.Handler.Handle(
            BalanceSucceeded(Deadline.AddSeconds(-1)),
            CancellationToken.None);

        handled.Should().BeTrue();
        fixture.Outbox.Events.Should().HaveCount(2);
        var refundEvent = fixture.Outbox.Events.Single(evt => evt.EventType == ParcelOutboxEvents.RefundInitiated);
        using var refundPayload = JsonDocument.Parse(refundEvent.PayloadJson);
        refundPayload.RootElement.GetProperty("amount").GetInt64().Should().Be(10_000);
        refundPayload.RootElement.GetProperty("idempotencyKey").GetString()
            .Should().Be($"{ParcelId:D}:PAYMENT_CALLBACK_DELAY_CANNOT_SERVE");
        var recoveredEvent = fixture.Outbox.Events.Single(evt => evt.EventType == ParcelOutboxEvents.SettlementRecovered);
        using var recoveredPayload = JsonDocument.Parse(recoveredEvent.PayloadJson);
        recoveredPayload.RootElement.GetProperty("eventId").GetGuid().Should().Be(recoveredEvent.EventId);
        recoveredPayload.RootElement.GetProperty("recoveredStatus").GetString().Should().Be("CANCELLED");
        recoveredPayload.RootElement.GetProperty("refundAmountVnd").GetInt64().Should().Be(10_000);
    }

    private static CallbackFixture CreateCallbackFixture(ParcelEntity parcel)
    {
        var repository = Substitute.For<IParcelRepository>();
        repository.GetByIdAsync(ParcelId, Arg.Any<CancellationToken>()).Returns(parcel);
        var outbox = new RecordingOutbox();
        var stats = Substitute.For<IParcelStatsRepository>();
        var trip = Substitute.For<ITripServiceClient, IIdempotentTripServiceClient>();
        var handler = new ConfirmPaymentForParcelCommandHandler(
            repository,
            outbox,
            stats,
            trip,
            Clock(),
            Substitute.For<ILogger<ConfirmPaymentForParcelCommandHandler>>());
        return new CallbackFixture(
            handler,
            repository,
            trip,
            (IIdempotentTripServiceClient)trip,
            outbox);
    }

    private static ParcelEntity CreateParcel(
        ParcelStatus status,
        string? rejectionReason = null)
    {
        var parcel = ParcelEntity.CreatePendingPayment(
            "VRP-20260727-FINAL001",
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
            Deadline.AddMinutes(10),
            Now.AddMinutes(-10));
        SetPrivateProperty(parcel, nameof(ParcelEntity.Status), status);
        SetPrivateProperty(parcel, nameof(ParcelEntity.DepositPaidVnd), Money.FromRaw(2_000));
        SetPrivateProperty(parcel, nameof(ParcelEntity.BalanceRequiredVnd), Money.FromRaw(8_000));
        SetPrivateProperty(parcel, nameof(ParcelEntity.FinalPaymentDeadline), (DateTimeOffset?)Deadline);
        SetPrivateProperty(parcel, nameof(ParcelEntity.ActualWeightKg), (decimal?)10m);
        SetPrivateProperty(parcel, nameof(ParcelEntity.ActualVolumeM3), (decimal?)0.001m);
        SetPrivateProperty(parcel, nameof(ParcelEntity.RejectionReason), rejectionReason);
        return parcel;
    }

    private static ConfirmPaymentForParcelCommand BalanceSucceeded(DateTimeOffset paidAt)
        => new(
            PaymentId,
            "PARCEL_ADDITIONAL",
            ParcelId,
            8_000,
            "VNPAY",
            paidAt,
            Deadline);

    private static IClock Clock()
    {
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        return clock;
    }

    private static ParcelPaymentTransitionSnapshot Snapshot(ParcelStatus status)
        => new(
            ParcelId,
            "VRP-20260727-FINAL001",
            status,
            DepositAmount: 2_000,
            AdditionalAmount: 8_000,
            OperatorId,
            TripId,
            BookingId: null,
            SenderUserId,
            ParcelSizeCategory.MEDIUM,
            AdditionalPaymentId: PaymentId);

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

    private sealed record CallbackFixture(
        ConfirmPaymentForParcelCommandHandler Handler,
        IParcelRepository Repository,
        ITripServiceClient Trip,
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
