using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Parcel.Application.Features.Parcels.Review;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.ValueObjects;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.UnitTests.Features;

public sealed class ReviewParcelTests
{
    private static readonly Guid ParcelId = Guid.NewGuid();
    private static readonly Guid OperatorId = Guid.NewGuid();
    private static readonly Guid TripId = Guid.NewGuid();
    private static readonly Guid SenderUserId = Guid.NewGuid();

    private static ParcelEntity CreatePendingReviewParcel()
    {
        return ParcelEntity.CreatePendingOperatorReview(
            "VRP-001", SenderUserId, Guid.NewGuid(), "Recipient",
            PhoneNumber.Normalize("+84912345678"), "r@example.com",
            OperatorId, TripId, Guid.NewGuid(), null, "Item", "",
            ParcelSizeCategory.EXTRA_LARGE, 10m,
            ParcelDeliveryMethod.TERMINAL_PICKUP, Money.FromRaw(200_000));
    }

    [Fact]
    public async Task ReviewParcel_Approve_Success()
    {
        var parcel = CreatePendingReviewParcel();
        var repo = Substitute.For<IParcelRepository>();
        repo.GetByIdAsync(ParcelId, Arg.Any<CancellationToken>()).Returns(parcel);
        repo.TryApproveReviewAsync(ParcelId, OperatorId, Money.FromRaw(200_000), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new ParcelPaymentTransitionSnapshot(ParcelId, "VRP-001", ParcelStatus.PENDING_PAYMENT,
                200_000, 0, OperatorId, TripId, null, SenderUserId, ParcelSizeCategory.EXTRA_LARGE, null));

        var outbox = new RecordingOutbox();
        var handler = new ReviewParcelCommandHandler(repo, UnitOfWork(), outbox, Stats());
        var result = await handler.Handle(new ReviewParcelCommand(
            ParcelId, OperatorId, OperatorId, "APPROVED", null), default);

        result.Status.Should().Be("PENDING_PAYMENT");
        result.DepositAmount.Should().Be(200_000);
        outbox.Events.Should().ContainSingle();
        var integrationEvent = outbox.Events.Single();
        integrationEvent.EventType.Should().Be(ParcelOutboxEvents.ReviewApproved);
        using var payload = JsonDocument.Parse(integrationEvent.PayloadJson);
        payload.RootElement.GetProperty("eventId").GetGuid().Should().Be(integrationEvent.EventId);
        payload.RootElement.GetProperty("parcelId").GetGuid().Should().Be(ParcelId);
        payload.RootElement.GetProperty("parcelCode").GetString().Should().Be("VRP-001");
        payload.RootElement.GetProperty("operatorId").GetGuid().Should().Be(OperatorId);
        payload.RootElement.GetProperty("userId").GetGuid().Should().Be(SenderUserId);
        payload.RootElement.GetProperty("depositRequiredVnd").GetInt64().Should().Be(200_000);
    }

    [Fact]
    public async Task ReviewParcel_Reject_Success()
    {
        var parcel = CreatePendingReviewParcel();
        var repo = Substitute.For<IParcelRepository>();
        repo.GetByIdAsync(ParcelId, Arg.Any<CancellationToken>()).Returns(parcel);
        repo.TryRejectReviewAsync(ParcelId, OperatorId, "Overweight", Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns(new ParcelPaymentTransitionSnapshot(ParcelId, "VRP-001", ParcelStatus.REJECTED,
                0, 0, OperatorId, TripId, null, SenderUserId, ParcelSizeCategory.EXTRA_LARGE, null));

        var outbox = new RecordingOutbox();
        var handler = new ReviewParcelCommandHandler(repo, UnitOfWork(), outbox, Stats());
        var result = await handler.Handle(new ReviewParcelCommand(
            ParcelId, OperatorId, OperatorId, "REJECTED", "  Overweight  "), default);

        result.Status.Should().Be("REJECTED");
        outbox.Events.Should().ContainSingle();
        var integrationEvent = outbox.Events.Single();
        integrationEvent.EventType.Should().Be(ParcelOutboxEvents.Rejected);
        using var payload = JsonDocument.Parse(integrationEvent.PayloadJson);
        payload.RootElement.GetProperty("eventId").GetGuid().Should().Be(integrationEvent.EventId);
        payload.RootElement.GetProperty("parcelId").GetGuid().Should().Be(ParcelId);
        payload.RootElement.GetProperty("operatorId").GetGuid().Should().Be(OperatorId);
        payload.RootElement.GetProperty("userId").GetGuid().Should().Be(SenderUserId);
        payload.RootElement.GetProperty("tripId").GetGuid().Should().Be(TripId);
        payload.RootElement.GetProperty("reason").GetString().Should().Be("Overweight");
        await repo.Received(1).TryRejectReviewAsync(
            ParcelId,
            OperatorId,
            "Overweight",
            Arg.Any<DateTimeOffset>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ReviewParcel_ParcelNotFound_Throws()
    {
        var repo = Substitute.For<IParcelRepository>();
        repo.GetByIdAsync(ParcelId, Arg.Any<CancellationToken>()).Returns((ParcelEntity?)null);

        var handler = new ReviewParcelCommandHandler(repo, UnitOfWork(), Outbox(), Stats());
        var act = () => handler.Handle(new ReviewParcelCommand(
            ParcelId, OperatorId, OperatorId, "APPROVED", null), default);

        await act.Should().ThrowAsync<CodedNotFoundException>()
            .Where(e => e.ErrorCode == "PARCEL_NOT_FOUND");
    }

    private static IUnitOfWork UnitOfWork()
        => Substitute.For<IUnitOfWork>();

    private static IIntegrationEventOutbox Outbox()
        => Substitute.For<IIntegrationEventOutbox>();

    private static IParcelStatsRepository Stats()
        => Substitute.For<IParcelStatsRepository>();

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
