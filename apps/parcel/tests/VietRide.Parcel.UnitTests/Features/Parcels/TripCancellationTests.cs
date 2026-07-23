using System.Text.Json;
using FluentAssertions;
using NSubstitute;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Parcel.Application.Features.Parcels.TripCancellationImpact;
using VietRide.Parcel.Application.Features.Parcels.TripEvents;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Outbox;

namespace VietRide.Parcel.UnitTests.Features.Parcels;

public sealed class TripCancellationTests
{
    [Fact]
    public async Task ImpactQuery_MapsRepositoryProjection()
    {
        var tripId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        var parcelId = Guid.NewGuid();
        var repository = Substitute.For<IParcelRepository>();
        repository.GetTripCancellationImpactAsync(tripId, operatorId, Arg.Any<CancellationToken>())
            .Returns([new TripCancellationParcelImpact(parcelId, "PENDING", 175_000)]);

        var result = await new GetTripCancellationImpactQueryHandler(repository)
            .Handle(new GetTripCancellationImpactQuery(tripId, operatorId), CancellationToken.None);

        result.TripId.Should().Be(tripId);
        result.AffectedParcels.Should().ContainSingle().Which.Should().BeEquivalentTo(
            new TripCancellationImpactResponse.AffectedParcel(parcelId, "PENDING", 175_000));
    }

    [Fact]
    public async Task OperatorTripCancellation_RefundsAllCollectedParcelMoney()
    {
        var tripId = Guid.NewGuid();
        var parcelId = Guid.NewGuid();
        var operatorId = Guid.NewGuid();
        var senderUserId = Guid.NewGuid();
        var repository = Substitute.For<IParcelRepository>();
        var tripClient = Substitute.For<ITripServiceClient>();
        var outbox = Substitute.For<IIntegrationEventOutbox>();
        var stats = Substitute.For<IParcelStatsRepository>();
        var events = new List<(string Type, string Payload)>();
        var snapshot = new ParcelEventSnapshot(
            parcelId,
            "VRP-001",
            operatorId,
            tripId,
            ParcelStatus.CANCELLED,
            100_000,
            25_000,
            senderUserId);
        repository.TryRejectPreAcceptanceByTripIdAsync(tripId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([]);
        repository.TryCancelPendingByTripIdAsync(tripId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([snapshot]);
        repository.TryBulkSetPendingOperatorActionByTripIdAsync(tripId, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
            .Returns([]);
        tripClient.ReleaseCargoAsync(tripId, parcelId, 0m, 0.0001m, Arg.Any<CancellationToken>())
            .Returns(new TripCargoOutcome(TripCargoOutcomeKind.Success, null));
        outbox.EnqueueAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask)
            .AndDoes(call => events.Add((call.ArgAt<string>(0), call.ArgAt<string>(1))));
        stats.UpsertIncrementAsync(
                operatorId,
                Arg.Any<DateOnly>(),
                0, 0, 0, 1, 0, 0, 125_000,
                Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var count = await new HandleTripCancelledCommandHandler(repository, tripClient, outbox, stats)
            .Handle(new HandleTripCancelledCommand(tripId), CancellationToken.None);

        count.Should().Be(1);
        events.Should().HaveCount(2);
        ReadAmount(events.Single(item => item.Type == ParcelOutboxEvents.Cancelled).Payload)
            .Should().Be(125_000);
        ReadAmount(events.Single(item => item.Type == ParcelOutboxEvents.RefundInitiated).Payload)
            .Should().Be(125_000);
    }

    private static long ReadAmount(string payload)
    {
        using var json = JsonDocument.Parse(payload);
        return json.RootElement.GetProperty(
            json.RootElement.TryGetProperty("refundAmount", out _) ? "refundAmount" : "amount").GetInt64();
    }
}
