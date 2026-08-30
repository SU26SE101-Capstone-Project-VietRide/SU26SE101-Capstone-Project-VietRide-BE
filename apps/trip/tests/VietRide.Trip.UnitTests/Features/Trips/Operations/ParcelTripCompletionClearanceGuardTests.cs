using FluentAssertions;
using VietRide.Shared.Application.Exceptions;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Features.Trips.Operations;

namespace VietRide.Trip.UnitTests.Features.Trips.Operations;

public sealed class ParcelTripCompletionClearanceGuardTests
{
    [Fact]
    public async Task BlockedReconciliation_RejectsCompletionWithStructuredAction()
    {
        var parcelId = Guid.NewGuid();
        var client = new FakeClient(new ParcelTripCompletionClearanceProjection(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "BLOCKED_RECONCILIATION",
            [parcelId],
            []));

        var action = () => ParcelTripCompletionClearanceGuard.EnsureAsync(
            client,
            client.Projection.TripId,
            client.Projection.OperatorId,
            allowAcknowledgedIncidents: false,
            CancellationToken.None);

        var exception = await action.Should().ThrowAsync<CodedConflictException>();
        exception.Which.ErrorCode.Should().Be("PARCEL_DESTINATION_RECONCILIATION_REQUIRED");
        exception.Which.Errors.Should().Contain(error =>
            error.Field == "requiredAction"
            && error.Message == "RECONCILE_DESTINATION_PARCELS");
    }

    [Fact]
    public async Task AcknowledgedIncidents_RequireDriverConfirmation()
    {
        var client = new FakeClient(new ParcelTripCompletionClearanceProjection(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "ACKNOWLEDGED_INCIDENTS",
            [Guid.NewGuid()],
            [Guid.NewGuid()]));

        await FluentActions.Invoking(() => ParcelTripCompletionClearanceGuard.EnsureAsync(
                client,
                client.Projection.TripId,
                client.Projection.OperatorId,
                allowAcknowledgedIncidents: false,
                CancellationToken.None))
            .Should().ThrowAsync<CodedConflictException>();

        await FluentActions.Invoking(() => ParcelTripCompletionClearanceGuard.EnsureAsync(
                client,
                client.Projection.TripId,
                client.Projection.OperatorId,
                allowAcknowledgedIncidents: true,
                CancellationToken.None))
            .Should().NotThrowAsync();
    }

    private sealed class FakeClient(ParcelTripCompletionClearanceProjection projection)
        : IParcelImpactClient
    {
        public ParcelTripCompletionClearanceProjection Projection { get; } = projection;

        public Task<ParcelTripCompletionClearanceProjection> GetTripCompletionClearanceAsync(
            Guid tripId,
            Guid operatorId,
            CancellationToken cancellationToken)
            => Task.FromResult(Projection);

        public Task<ParcelStopDepartureClearanceProjection> GetStopDepartureClearanceAsync(
            Guid tripId,
            Guid stopId,
            Guid operatorId,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();

        public Task<TripParcelCancellationImpactProjection> GetTripCancellationImpactAsync(
            Guid tripId,
            Guid operatorId,
            CancellationToken cancellationToken)
            => throw new NotSupportedException();
    }
}
