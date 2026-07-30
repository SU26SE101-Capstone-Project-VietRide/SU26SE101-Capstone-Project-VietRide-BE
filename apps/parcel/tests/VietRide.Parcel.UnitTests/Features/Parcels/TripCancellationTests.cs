using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Parcel.Application.Features.Parcels.TripCancellationImpact;
using VietRide.Parcel.Application.Features.Parcels.TripEvents;
using VietRide.Parcel.Domain.Enums;
using VietRide.Parcel.Infrastructure.Messaging;
using VietRide.Shared.Application.Outbox;

namespace VietRide.Parcel.UnitTests.Features.Parcels;

public sealed class TripCancellationTests
{
    [Theory]
    [InlineData(ParcelStatus.PENDING_OPERATOR_REVIEW)]
    [InlineData(ParcelStatus.PENDING_PAYMENT)]
    [InlineData(ParcelStatus.PENDING)]
    [InlineData(ParcelStatus.PENDING_ADDITIONAL_PAYMENT)]
    [InlineData(ParcelStatus.RESERVED)]
    [InlineData(ParcelStatus.CHECKED_IN)]
    [InlineData(ParcelStatus.PENDING_FINAL_PAYMENT)]
    [InlineData(ParcelStatus.READY_TO_LOAD)]
    public void Classifier_PreLoad_CancelsWithOutstandingCollected(ParcelStatus status)
    {
        var result = ParcelTripCancellationClassifier.Classify(
            status,
            depositPaidVnd: 100_000,
            balancePaidVnd: 50_000,
            refundedAmountVnd: 25_000);

        result.Disposition.Should().Be(
            ParcelTripCancellationDisposition.CancelAndRefund);
        result.TargetStatus.Should().Be(ParcelStatus.CANCELLED);
        result.RefundAmountVnd.Should().Be(125_000);
    }

    [Theory]
    [InlineData(ParcelStatus.LOADED)]
    [InlineData(ParcelStatus.IN_TRANSIT)]
    public void Classifier_PhysicalCargo_DefersRefundAndRelease(ParcelStatus status)
    {
        var result = ParcelTripCancellationClassifier.Classify(
            status,
            100_000,
            50_000,
            0);

        result.Disposition.Should().Be(
            ParcelTripCancellationDisposition.PendingOperatorAction);
        result.TargetStatus.Should().Be(ParcelStatus.PENDING_OPERATOR_ACTION);
        result.RefundAmountVnd.Should().Be(0);
    }

    [Theory]
    [InlineData(ParcelStatus.PENDING_OPERATOR_ACTION)]
    [InlineData(ParcelStatus.PENDING_TRANSFER_CONFIRM)]
    [InlineData(ParcelStatus.DELIVERY_CONFIRMED)]
    [InlineData(ParcelStatus.CANCELLED)]
    [InlineData(ParcelStatus.REJECTED)]
    [InlineData(ParcelStatus.RETURNED)]
    public void Classifier_ReplayOrTerminal_IsNoOp(ParcelStatus status)
    {
        var result = ParcelTripCancellationClassifier.Classify(
            status,
            100_000,
            50_000,
            0);

        result.Disposition.Should().Be(ParcelTripCancellationDisposition.None);
        result.TargetStatus.Should().BeNull();
        result.RefundAmountVnd.Should().Be(0);
    }

    [Fact]
    public async Task ImpactQuery_UsesSharedClassifierAndOmitsTerminalRows()
    {
        var tripId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        var active = Candidate(
            tripId,
            operatorId,
            ParcelStatus.RESERVED,
            depositPaidVnd: 200_000,
            balancePaidVnd: 50_000,
            refundedAmountVnd: 75_000);
        var terminal = Candidate(
            tripId,
            operatorId,
            ParcelStatus.CANCELLED);
        var repository = Substitute.For<IParcelRepository>();
        repository.GetTripCancellationCandidatesAsync(
                tripId,
                operatorId,
                Arg.Any<CancellationToken>())
            .Returns([active, terminal]);

        var result = await new GetTripCancellationImpactQueryHandler(repository)
            .Handle(
                new GetTripCancellationImpactQuery(tripId, operatorId),
                CancellationToken.None);

        result.TripId.Should().Be(tripId);
        result.AffectedParcels.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new TripCancellationImpactResponse.AffectedParcel(
                active.ParcelId,
                "RESERVED",
                175_000));
    }

    [Fact]
    public async Task OperatorTripCancellation_IsTenantScopedAndRefundsOutstandingOnce()
    {
        var sourceEventId = Guid.NewGuid();
        var tripId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        var candidate = Candidate(
            tripId,
            operatorId,
            ParcelStatus.CHECKED_IN,
            depositPaidVnd: 100_000,
            balancePaidVnd: 50_000,
            refundedAmountVnd: 25_000);
        var repository = Substitute.For<IParcelRepository>();
        repository.GetTripCancellationCandidatesAsync(
                tripId,
                operatorId,
                Arg.Any<CancellationToken>())
            .Returns([candidate]);
        repository.TryApplyTripCancellationAsync(
                candidate.ParcelId,
                operatorId,
                ParcelStatus.CHECKED_IN,
                ParcelStatus.CANCELLED,
                150_000,
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(true);

        var tripClient = Substitute.For<ITripServiceClient, IIdempotentTripServiceClient>();
        var idempotentTripClient = (IIdempotentTripServiceClient)tripClient;
        idempotentTripClient.ReleaseCargoAsync(
                tripId,
                candidate.ParcelId,
                candidate.ActualWeightKg!.Value,
                candidate.ActualVolumeM3!.Value,
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>())
            .Returns(new TripCargoOutcome(TripCargoOutcomeKind.Success, null));
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        var events = new List<(Guid Id, string Type, string Payload)>();
        outbox.EnqueueAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(call => events.Add((
                call.ArgAt<Guid>(0),
                call.ArgAt<string>(1),
                call.ArgAt<string>(2))));
        var stats = Substitute.For<IParcelStatsRepository>();

        var command = new HandleTripCancelledCommand(
            sourceEventId,
            DateTimeOffset.UtcNow,
            tripId,
            operatorId,
            DateTimeOffset.UtcNow,
            "Vehicle issue");
        var count = await new HandleTripCancelledCommandHandler(
                repository,
                tripClient,
                outbox,
                stats)
            .Handle(command, CancellationToken.None);

        count.Should().Be(1);
        events.Should().ContainSingle(item =>
            item.Type == ParcelOutboxEvents.Cancelled);
        var refund = events.Should().ContainSingle(item =>
            item.Type == ParcelOutboxEvents.RefundInitiated).Subject;
        using var refundJson = JsonDocument.Parse(refund.Payload);
        refundJson.RootElement.GetProperty("amount").GetInt64().Should().Be(125_000);
        refundJson.RootElement.GetProperty("reason").GetString()
            .Should().Be("TRIP_CANCELLED_PRE_LOAD");
        Guid.Parse(refundJson.RootElement.GetProperty("idempotencyKey").GetString()!)
            .ToString("D")[14].Should().Be('4');
        await idempotentTripClient.Received(1).ReleaseCargoAsync(
            tripId,
            candidate.ParcelId,
            candidate.ActualWeightKg.Value,
            candidate.ActualVolumeM3.Value,
            Arg.Is<Guid>(key => key.ToString("D")[14] == '4'),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OperatorTripCancellation_LoadedCargoOnlyMovesToPendingAction()
    {
        var tripId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        var candidate = Candidate(
            tripId,
            operatorId,
            ParcelStatus.LOADED,
            depositPaidVnd: 100_000);
        var repository = Substitute.For<IParcelRepository>();
        repository.GetTripCancellationCandidatesAsync(
                tripId,
                operatorId,
                Arg.Any<CancellationToken>())
            .Returns([candidate]);
        repository.TryApplyTripCancellationAsync(
                candidate.ParcelId,
                operatorId,
                ParcelStatus.LOADED,
                ParcelStatus.PENDING_OPERATOR_ACTION,
                0,
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(true);
        var tripClient = Substitute.For<ITripServiceClient, IIdempotentTripServiceClient>();
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        var events = new List<string>();
        outbox.EnqueueAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(call => events.Add(call.ArgAt<string>(1)));

        var count = await new HandleTripCancelledCommandHandler(
                repository,
                tripClient,
                outbox,
                Substitute.For<IParcelStatsRepository>())
            .Handle(
                new HandleTripCancelledCommand(
                    Guid.NewGuid(),
                    DateTimeOffset.UtcNow,
                    tripId,
                    operatorId,
                    DateTimeOffset.UtcNow,
                    "Vehicle issue"),
                CancellationToken.None);

        count.Should().Be(1);
        events.Should().Equal(ParcelOutboxEvents.PendingOperatorAction);
        await ((IIdempotentTripServiceClient)tripClient).DidNotReceiveWithAnyArgs()
            .ReleaseCargoAsync(default, default, default, default, default);
    }

    [Fact]
    public async Task NoSubstitutionDisruption_ReusesCancellationClassifier()
    {
        var tripId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        var candidate = Candidate(
            tripId,
            operatorId,
            ParcelStatus.RESERVED,
            depositPaidVnd: 90_001,
            refundedAmountVnd: 1);
        var repository = Substitute.For<IParcelRepository>();
        repository.GetTripCancellationCandidatesAsync(
                tripId,
                operatorId,
                Arg.Any<CancellationToken>())
            .Returns([candidate]);
        repository.TryApplyTripCancellationAsync(
                candidate.ParcelId,
                operatorId,
                ParcelStatus.RESERVED,
                ParcelStatus.CANCELLED,
                90_001,
                Arg.Any<DateTimeOffset>(),
                Arg.Any<CancellationToken>())
            .Returns(true);
        var tripClient = Substitute.For<ITripServiceClient, IIdempotentTripServiceClient>();
        ((IIdempotentTripServiceClient)tripClient).ReleaseCargoAsync(
                Arg.Any<Guid>(),
                Arg.Any<Guid>(),
                Arg.Any<decimal>(),
                Arg.Any<decimal>(),
                Arg.Any<Guid>(),
                Arg.Any<CancellationToken>())
            .Returns(new TripCargoOutcome(TripCargoOutcomeKind.Success, null));
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        var payloads = new List<(string Type, string Payload)>();
        outbox.EnqueueAsync(
                Arg.Any<Guid>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(call => payloads.Add((
                call.ArgAt<string>(1),
                call.ArgAt<string>(2))));

        var count = await new HandleTripDisruptedCommandHandler(
                repository,
                tripClient,
                outbox,
                Substitute.For<IParcelStatsRepository>())
            .Handle(
                new HandleTripDisruptedCommand(
                    Guid.NewGuid(),
                    DateTimeOffset.UtcNow,
                    tripId,
                    operatorId,
                    DateTimeOffset.UtcNow,
                    HasSubstitution: false,
                    "No replacement vehicle"),
                CancellationToken.None);

        count.Should().Be(1);
        var refundPayload = payloads.Single(item =>
            item.Type == ParcelOutboxEvents.RefundInitiated).Payload;
        using var json = JsonDocument.Parse(refundPayload);
        json.RootElement.GetProperty("amount").GetInt64().Should().Be(90_000);
        json.RootElement.GetProperty("reason").GetString()
            .Should().Be("TRIP_DISRUPTED_PRE_LOAD");
    }

    [Fact]
    public void TripDisruptedContract_RejectsLegacyTripWideRatio()
    {
        const string payload = """
            {
              "eventId": "11111111-1111-4111-8111-111111111111",
              "occurredAt": "2026-07-30T10:00:00Z",
              "tripId": "22222222-2222-4222-8222-222222222222",
              "operatorId": "33333333-3333-4333-8333-333333333333",
              "terminalAt": "2026-07-30T10:00:00Z",
              "hasSubstitution": false,
              "traveledRatio": 0.5
            }
            """;

        var act = () => JsonSerializer.Deserialize<TripDisruptedIntegrationEvent>(
            payload,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));

        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void OperationIds_AreStableDistinctUuidV4()
    {
        var sourceId = Guid.NewGuid();
        var parcelId = Guid.NewGuid();

        var first = ParcelOperationId.Create(sourceId, parcelId, "refund");
        var replay = ParcelOperationId.Create(sourceId, parcelId, "refund");
        var otherPhase = ParcelOperationId.Create(sourceId, parcelId, "cargo-release");

        replay.Should().Be(first);
        otherPhase.Should().NotBe(first);
        first.ToString("D")[14].Should().Be('4');
    }

    private static TripCancellationParcelCandidate Candidate(
        Guid tripId,
        Guid operatorId,
        ParcelStatus status,
        long depositPaidVnd = 0,
        long balancePaidVnd = 0,
        long refundedAmountVnd = 0)
        => new(
            Guid.NewGuid(),
            "VRP-001",
            operatorId,
            tripId,
            status,
            depositPaidVnd,
            balancePaidVnd,
            refundedAmountVnd,
            Guid.NewGuid(),
            10m,
            0.1m,
            12m,
            0.2m);
}
