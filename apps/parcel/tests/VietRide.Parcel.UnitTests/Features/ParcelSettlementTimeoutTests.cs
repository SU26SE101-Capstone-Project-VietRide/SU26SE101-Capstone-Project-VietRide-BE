using System.Reflection;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Parcel.Application.Features.Parcels.ExpireSettlementTimeouts;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.UnitTests.Features;

public sealed class ParcelSettlementTimeoutTests
{
    private static readonly Guid ParcelId = Guid.NewGuid();
    private static readonly Guid SenderUserId = Guid.NewGuid();
    private static readonly Guid OperatorId = Guid.NewGuid();
    private static readonly Guid TripId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 7, 27, 1, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CheckInTimeout_ForfeitsDepositAndReleasesEstimatedCargo()
    {
        var fixture = CreateFixture(ParcelStatus.RESERVED);
        fixture.Repository.ListCheckInTimedOutIdsAsync(
                Now,
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns([ParcelId]);
        fixture.Repository.ListFinalPaymentTimedOutIdsAsync(
                Now,
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns([]);
        fixture.Repository.TryRejectCheckInTimedOutAsync(
                ParcelId,
                "CHECK_IN_TIMEOUT",
                Now,
                Arg.Any<CancellationToken>())
            .Returns(Snapshot(ParcelStatus.REJECTED));

        var count = await fixture.Handler.Handle(
            new ExpireParcelSettlementTimeoutsCommand(),
            CancellationToken.None);

        count.Should().Be(1);
        await fixture.IdempotentTrip.Received(1).ReleaseCargoAsync(
            TripId,
            ParcelId,
            2m,
            Arg.Any<decimal>(),
            ParcelId,
            Arg.Any<CancellationToken>());
        AssertForfeitureEvent(fixture.Outbox, "CHECK_IN_TIMEOUT");
    }

    [Fact]
    public async Task FinalPaymentTimeout_ForfeitsDepositAndReleasesActualCargo()
    {
        var fixture = CreateFixture(ParcelStatus.PENDING_FINAL_PAYMENT, actualWeightKg: 3.2m);
        fixture.Repository.ListCheckInTimedOutIdsAsync(
                Now,
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns([]);
        fixture.Repository.ListFinalPaymentTimedOutIdsAsync(
                Now,
                Arg.Any<int>(),
                Arg.Any<CancellationToken>())
            .Returns([ParcelId]);
        fixture.Repository.TryRejectFinalPaymentTimedOutAsync(
                ParcelId,
                "FINAL_PAYMENT_TIMEOUT",
                Now,
                Arg.Any<CancellationToken>())
            .Returns(Snapshot(ParcelStatus.REJECTED));

        var count = await fixture.Handler.Handle(
            new ExpireParcelSettlementTimeoutsCommand(),
            CancellationToken.None);

        count.Should().Be(1);
        await fixture.IdempotentTrip.Received(1).ReleaseCargoAsync(
            TripId,
            ParcelId,
            3.2m,
            Arg.Any<decimal>(),
            ParcelId,
            Arg.Any<CancellationToken>());
        AssertForfeitureEvent(fixture.Outbox, "FINAL_PAYMENT_TIMEOUT");
    }

    private static Fixture CreateFixture(ParcelStatus status, decimal? actualWeightKg = null)
    {
        var parcel = CreateParcel(status, actualWeightKg);
        var repository = Substitute.For<IParcelRepository>();
        repository.GetByIdAsync(ParcelId, Arg.Any<CancellationToken>()).Returns(parcel);
        var trip = Substitute.For<ITripServiceClient, IIdempotentTripServiceClient>();
        var idempotentTrip = (IIdempotentTripServiceClient)trip;
        idempotentTrip.ReleaseCargoAsync(
                TripId,
                ParcelId,
                Arg.Any<decimal>(),
                Arg.Any<decimal>(),
                ParcelId,
                Arg.Any<CancellationToken>())
            .Returns(new TripCargoOutcome(TripCargoOutcomeKind.Success, null));
        var outbox = new RecordingOutbox();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        var handler = new ExpireParcelSettlementTimeoutsCommandHandler(
            repository,
            trip,
            outbox,
            Substitute.For<IParcelStatsRepository>(),
            unitOfWork,
            clock,
            Substitute.For<ILogger<ExpireParcelSettlementTimeoutsCommandHandler>>());
        return new Fixture(handler, repository, idempotentTrip, outbox);
    }

    private static ParcelEntity CreateParcel(ParcelStatus status, decimal? actualWeightKg)
    {
        var parcel = ParcelEntity.CreatePendingPayment(
            "VRP-20260727-TIMEOUT1",
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
            totalPrice: Money.FromRaw(10_000),
            depositPercent: 20m,
            depositAmount: Money.FromRaw(2_000));
        SetPrivateProperty(parcel, nameof(ParcelEntity.Id), ParcelId);
        parcel.ConfigureSettlementV2(
            ParcelSizeCategory.SMALL,
            Money.FromRaw(10_000),
            Money.Zero,
            Money.FromRaw(10_000),
            20m,
            Money.FromRaw(2_000),
            Money.FromRaw(1_000),
            Money.Zero,
            ParcelCargoCalculator.DefaultDimWeightFactor,
            Now,
            Now.AddMinutes(-10));
        SetPrivateProperty(parcel, nameof(ParcelEntity.Status), status);
        SetPrivateProperty(parcel, nameof(ParcelEntity.DepositPaidVnd), Money.FromRaw(2_000));
        if (actualWeightKg.HasValue)
        {
            SetPrivateProperty(parcel, nameof(ParcelEntity.ActualWeightKg), actualWeightKg);
            SetPrivateProperty(parcel, nameof(ParcelEntity.ActualVolumeM3), (decimal?)0.002m);
        }

        return parcel;
    }

    private static ParcelPaymentTransitionSnapshot Snapshot(ParcelStatus status)
        => new(
            ParcelId,
            "VRP-20260727-TIMEOUT1",
            status,
            DepositAmount: 2_000,
            AdditionalAmount: 0,
            OperatorId,
            TripId,
            BookingId: null,
            SenderUserId,
            ParcelSizeCategory.SMALL,
            AdditionalPaymentId: null);

    private static void AssertForfeitureEvent(RecordingOutbox outbox, string reason)
    {
        outbox.Events.Should().ContainSingle();
        using var payload = JsonDocument.Parse(outbox.Events.Single().PayloadJson);
        payload.RootElement.GetProperty("reason").GetString().Should().Be(reason);
        payload.RootElement.GetProperty("forfeitedDepositVnd").GetInt64().Should().Be(2_000);
        payload.RootElement.GetProperty("refundAmount").GetInt64().Should().Be(0);
    }

    private static void SetPrivateProperty<T>(ParcelEntity parcel, string propertyName, T value)
        => typeof(ParcelEntity)
            .GetProperty(propertyName, BindingFlags.Instance | BindingFlags.Public)!
            .SetValue(parcel, value);

    private sealed record Fixture(
        ExpireParcelSettlementTimeoutsCommandHandler Handler,
        IParcelRepository Repository,
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
