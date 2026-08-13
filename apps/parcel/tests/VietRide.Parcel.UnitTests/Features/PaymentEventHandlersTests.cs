using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Parcel.Application.Features.Parcels.ConfirmPaymentForParcel;
using VietRide.Parcel.Application.Features.Parcels.ExpirePaymentForParcel;
using VietRide.Parcel.Application.Features.Parcels.FailPaymentForParcel;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.UnitTests.Features;

public sealed class PaymentEventHandlersTests
{
    private static readonly Guid ParcelId = Guid.NewGuid();
    private static readonly Guid PaymentId = Guid.NewGuid();
    private static readonly DateTimeOffset Now = new(2026, 6, 29, 10, 0, 0, TimeSpan.FromHours(7));

    private static ParcelPaymentTransitionSnapshot MakeSnapshot(ParcelStatus status, long deposit = 100_000, long additional = 0)
        => new(ParcelId, "VRP-001", status, deposit, additional, Guid.NewGuid(),
            Guid.NewGuid(), null, Guid.NewGuid(), ParcelSizeCategory.MEDIUM, null);

    private static IParcelStatsRepository Stats()
        => Substitute.For<IParcelStatsRepository>();

    private static IIntegrationEventOutbox Outbox()
        => Substitute.For<IIntegrationEventOutbox>();

    private static IIdentityServiceClient Identity()
    {
        var identity = Substitute.For<IIdentityServiceClient>();
        identity.GetOperatorInfoAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns(call => new OperatorLookupOutcome(
                OperatorLookupOutcomeKind.Success,
                new IdentityOperatorInfo((Guid)call[0], "Operator", ParcelNoShowPolicy.Default),
                null));
        return identity;
    }

    private static ITripServiceClient Trip()
    {
        var trip = Substitute.For<ITripServiceClient>();
        trip.ReserveCargoAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(new TripCargoOutcome(TripCargoOutcomeKind.Success, null));
        return trip;
    }

    [Fact]
    public async Task EnqueueRefundAsync_UsesCanonicalParcelRefundPayload()
    {
        var parcelId = Guid.NewGuid();
        var senderUserId = Guid.NewGuid();
        var outbox = new RecordingOutbox();

        await ParcelOutboxEvents.EnqueueRefundAsync(outbox, parcelId, senderUserId, 100_000, default);

        outbox.Events.Should().ContainSingle();
        outbox.Events.Single().EventType.Should().Be(ParcelOutboxEvents.RefundInitiated);
        using var payload = JsonDocument.Parse(outbox.Events.Single().PayloadJson);
        payload.RootElement.GetProperty("parcelId").GetGuid().Should().Be(parcelId);
        payload.RootElement.GetProperty("senderUserId").GetGuid().Should().Be(senderUserId);
        payload.RootElement.GetProperty("amount").GetInt64().Should().Be(100_000);
        payload.RootElement.GetProperty("referenceType").GetString().Should().Be("PARCEL_REFUND");
        payload.RootElement.GetProperty("referenceId").GetGuid().Should().Be(parcelId);
        payload.RootElement.GetProperty("idempotencyKey").GetString().Should().Be(parcelId.ToString("D"));
    }

    [Fact]
    public void RefundAmountCalculator_AppliesNoShowFeePercent()
    {
        var amount = ParcelRefundAmountCalculator.ApplyNoShowFee(
            101,
            new ParcelNoShowPolicy(50, 30));

        amount.Should().Be(51);
    }

    [Fact]
    public async Task ConfirmPaymentForParcel_PARCEL_DepositSucceeded()
    {
        var repo = Substitute.For<IParcelRepository>();
        var clock = Substitute.For<IClock>();
        var outbox = new RecordingOutbox();
        clock.UtcNow.Returns(Now);
        repo.TryMarkDepositSucceededAsync(ParcelId, PaymentId, 100_000, Now, Arg.Any<CancellationToken>())
            .Returns(MakeSnapshot(ParcelStatus.RESERVED));

        var handler = new ConfirmPaymentForParcelCommandHandler(repo, outbox, Stats(), Trip(), clock,
            Substitute.For<ILogger<ConfirmPaymentForParcelCommandHandler>>());
        var result = await handler.Handle(new ConfirmPaymentForParcelCommand(PaymentId, "PARCEL", ParcelId, 100_000), default);

        result.Should().BeTrue();
        outbox.Events.Should().ContainSingle(evt => evt.EventType == ParcelOutboxEvents.Reserved);
        using var payload = JsonDocument.Parse(outbox.Events.Single().PayloadJson);
        payload.RootElement.GetProperty("parcelId").GetGuid().Should().Be(ParcelId);
        payload.RootElement.GetProperty("parcelCode").GetString().Should().Be("VRP-001");
        payload.RootElement.GetProperty("tripId").GetGuid().Should().NotBeEmpty();
        payload.RootElement.GetProperty("operatorId").GetGuid().Should().NotBeEmpty();
        payload.RootElement.GetProperty("senderUserId").GetGuid().Should().NotBeEmpty();
        payload.RootElement.GetProperty("eventId").GetGuid().Should().NotBeEmpty();
        payload.RootElement.GetProperty("occurredAt").GetDateTimeOffset().Should().Be(Now);
    }

    [Fact]
    public async Task ConfirmPaymentForParcel_CargoReservationFails_StillConfirmsPaymentAndEnqueuesOperatorAction()
    {
        var repo = Substitute.For<IParcelRepository>();
        var clock = Substitute.For<IClock>();
        var outbox = new RecordingOutbox();
        var trip = Substitute.For<ITripServiceClient>();
        clock.UtcNow.Returns(Now);
        repo.TryMarkDepositSucceededAsync(ParcelId, PaymentId, 100_000, Now, Arg.Any<CancellationToken>())
            .Returns(MakeSnapshot(ParcelStatus.PENDING));
        trip.ReserveCargoAsync(Arg.Any<Guid>(), Arg.Any<Guid>(), Arg.Any<decimal>(), Arg.Any<CancellationToken>())
            .Returns(new TripCargoOutcome(TripCargoOutcomeKind.TransportError, "Trip service unavailable."));

        var handler = new ConfirmPaymentForParcelCommandHandler(repo, outbox, Stats(), trip, clock,
            Substitute.For<ILogger<ConfirmPaymentForParcelCommandHandler>>());
        var result = await handler.Handle(new ConfirmPaymentForParcelCommand(PaymentId, "PARCEL", ParcelId, 100_000), default);

        result.Should().BeTrue();
        outbox.Events.Should().ContainSingle(evt => evt.EventType == ParcelOutboxEvents.PendingOperatorAction);
        outbox.Events.Should().NotContain(evt => evt.EventType == ParcelOutboxEvents.Reserved);
    }

    [Fact]
    public async Task ConfirmPaymentForParcel_PARCEL_ADDITIONAL_Succeeded()
    {
        var repo = Substitute.For<IParcelRepository>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        repo.TryMarkAdditionalSucceededAsync(ParcelId, 50_000, Arg.Any<Guid>(), Now, Arg.Any<CancellationToken>())
            .Returns(MakeSnapshot(ParcelStatus.PENDING));

        var handler = new ConfirmPaymentForParcelCommandHandler(repo, Outbox(), Stats(), Trip(), clock,
            Substitute.For<ILogger<ConfirmPaymentForParcelCommandHandler>>());
        var result = await handler.Handle(new ConfirmPaymentForParcelCommand(PaymentId, "PARCEL_ADDITIONAL", ParcelId, 50_000), default);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task ConfirmPaymentForParcel_UnrelatedReferenceType_ReturnsFalse()
    {
        var repo = Substitute.For<IParcelRepository>();
        var clock = Substitute.For<IClock>();
        var handler = new ConfirmPaymentForParcelCommandHandler(repo, Outbox(), Stats(), Trip(), clock,
            Substitute.For<ILogger<ConfirmPaymentForParcelCommandHandler>>());
        var result = await handler.Handle(new ConfirmPaymentForParcelCommand(PaymentId, "BOOKING", ParcelId, 100_000), default);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ConfirmPaymentForParcel_DepositAlreadySucceeded_ReturnsFalse()
    {
        var repo = Substitute.For<IParcelRepository>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        repo.TryMarkDepositSucceededAsync(ParcelId, PaymentId, 100_000, Now, Arg.Any<CancellationToken>())
            .Returns((ParcelPaymentTransitionSnapshot?)null);

        var handler = new ConfirmPaymentForParcelCommandHandler(repo, Outbox(), Stats(), Trip(), clock,
            Substitute.For<ILogger<ConfirmPaymentForParcelCommandHandler>>());
        var result = await handler.Handle(new ConfirmPaymentForParcelCommand(PaymentId, "PARCEL", ParcelId, 100_000), default);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ConfirmPaymentForParcel_LateSuccessOnTerminalParcel_EnqueuesRefund()
    {
        var repo = Substitute.For<IParcelRepository>();
        var clock = Substitute.For<IClock>();
        var outbox = new RecordingOutbox();
        clock.UtcNow.Returns(Now);
        repo.TryMarkDepositSucceededAsync(ParcelId, PaymentId, 100_000, Now, Arg.Any<CancellationToken>())
            .Returns((ParcelPaymentTransitionSnapshot?)null);
        repo.GetPaymentTransitionSnapshotAsync(ParcelId, Arg.Any<CancellationToken>())
            .Returns(MakeSnapshot(ParcelStatus.CANCELLED));

        var handler = new ConfirmPaymentForParcelCommandHandler(repo, outbox, Stats(), Trip(), clock,
            Substitute.For<ILogger<ConfirmPaymentForParcelCommandHandler>>());
        var result = await handler.Handle(new ConfirmPaymentForParcelCommand(PaymentId, "PARCEL", ParcelId, 100_000), default);

        result.Should().BeTrue();
        outbox.Events.Should().ContainSingle(evt => evt.EventType == ParcelOutboxEvents.RefundInitiated);
    }

    [Fact]
    public async Task ConfirmPaymentForParcel_AmountMismatch_DoesNotEnqueueRefund()
    {
        var repo = Substitute.For<IParcelRepository>();
        var clock = Substitute.For<IClock>();
        var outbox = new RecordingOutbox();
        clock.UtcNow.Returns(Now);
        repo.TryMarkDepositSucceededAsync(ParcelId, PaymentId, 90_000, Now, Arg.Any<CancellationToken>())
            .Returns((ParcelPaymentTransitionSnapshot?)null);
        repo.GetPaymentTransitionSnapshotAsync(ParcelId, Arg.Any<CancellationToken>())
            .Returns(MakeSnapshot(ParcelStatus.CANCELLED, deposit: 100_000));

        var handler = new ConfirmPaymentForParcelCommandHandler(repo, outbox, Stats(), Trip(), clock,
            Substitute.For<ILogger<ConfirmPaymentForParcelCommandHandler>>());
        var result = await handler.Handle(new ConfirmPaymentForParcelCommand(PaymentId, "PARCEL", ParcelId, 90_000), default);

        result.Should().BeFalse();
        outbox.Events.Should().BeEmpty();
    }

    [Fact]
    public async Task FailPaymentForParcel_PARCEL_DepositExpired()
    {
        var repo = Substitute.For<IParcelRepository>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        repo.TryMarkDepositFailedAsync(ParcelId, PaymentId, Now, Arg.Any<CancellationToken>())
            .Returns(MakeSnapshot(ParcelStatus.EXPIRED));

        var handler = new FailPaymentForParcelCommandHandler(repo, Identity(), Outbox(), Stats(), clock,
            Substitute.For<ILogger<FailPaymentForParcelCommandHandler>>());
        var result = await handler.Handle(new FailPaymentForParcelCommand(PaymentId, "PARCEL", ParcelId), default);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task FailPaymentForParcel_PARCEL_ADDITIONAL_Rejected()
    {
        var repo = Substitute.For<IParcelRepository>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        repo.TryMarkAdditionalFailedAsync(ParcelId, Now, Arg.Any<CancellationToken>())
            .Returns(MakeSnapshot(ParcelStatus.REJECTED));

        var handler = new FailPaymentForParcelCommandHandler(repo, Identity(), Outbox(), Stats(), clock,
            Substitute.For<ILogger<FailPaymentForParcelCommandHandler>>());
        var result = await handler.Handle(new FailPaymentForParcelCommand(PaymentId, "PARCEL_ADDITIONAL", ParcelId), default);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task FailPaymentForParcel_UnrelatedReferenceType_ReturnsFalse()
    {
        var repo = Substitute.For<IParcelRepository>();
        var clock = Substitute.For<IClock>();
        var handler = new FailPaymentForParcelCommandHandler(repo, Identity(), Outbox(), Stats(), clock,
            Substitute.For<ILogger<FailPaymentForParcelCommandHandler>>());
        var result = await handler.Handle(new FailPaymentForParcelCommand(PaymentId, "BOOKING", ParcelId), default);

        result.Should().BeFalse();
    }

    [Fact]
    public async Task ExpirePaymentForParcel_PARCEL_DepositExpired()
    {
        var repo = Substitute.For<IParcelRepository>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        repo.TryMarkDepositExpiredAsync(ParcelId, PaymentId, Now, Arg.Any<CancellationToken>())
            .Returns(MakeSnapshot(ParcelStatus.EXPIRED));

        var handler = new ExpirePaymentForParcelCommandHandler(repo, Identity(), Outbox(), Stats(), clock,
            Substitute.For<ILogger<ExpirePaymentForParcelCommandHandler>>());
        var result = await handler.Handle(new ExpirePaymentForParcelCommand(PaymentId, "PARCEL", ParcelId), default);

        result.Should().BeTrue();
    }

    [Fact]
    public async Task ExpirePaymentForParcel_PARCEL_ADDITIONAL_Rejected()
    {
        var repo = Substitute.For<IParcelRepository>();
        var clock = Substitute.For<IClock>();
        clock.UtcNow.Returns(Now);
        repo.TryMarkAdditionalExpiredAsync(ParcelId, Now, Arg.Any<CancellationToken>())
            .Returns(MakeSnapshot(ParcelStatus.REJECTED));

        var handler = new ExpirePaymentForParcelCommandHandler(repo, Identity(), Outbox(), Stats(), clock,
            Substitute.For<ILogger<ExpirePaymentForParcelCommandHandler>>());
        var result = await handler.Handle(new ExpirePaymentForParcelCommand(PaymentId, "PARCEL_ADDITIONAL", ParcelId), default);

        result.Should().BeTrue();
    }

    private sealed class RecordingOutbox : IIntegrationEventOutbox
    {
        public List<(string EventType, string PayloadJson)> Events { get; } = [];

        public Task EnqueueAsync(string eventType, string payloadJson, CancellationToken ct = default)
        {
            Events.Add((eventType, payloadJson));
            return Task.CompletedTask;
        }
    }
}
