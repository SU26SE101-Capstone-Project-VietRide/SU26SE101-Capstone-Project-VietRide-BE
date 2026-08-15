using FluentAssertions;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Application.Features.Internal.Trips.Tracking;
using VietRide.Trip.UnitTests.Features.Vehicles;

namespace VietRide.Trip.UnitTests.Features.Internal.Trips;

public sealed class ListOperatorTrackingShuttleTripsHandlerTests
{
    [Fact]
    public async Task Handle_DelegatesTheOperatorScopedActiveProjection()
    {
        var operatorId = Guid.NewGuid();
        IReadOnlyList<OperatorTrackingShuttleTripDto> expected =
        [
            new(Guid.NewGuid(), Guid.NewGuid(), "IN_PROGRESS"),
        ];
        Guid? capturedOperatorId = null;
        var service = TestProxy<IShuttleDispatchService>.Create((method, args) =>
        {
            if (method.Name != nameof(IShuttleDispatchService.GetTrackingProjectionAsync))
                return null;

            capturedOperatorId = Assert.IsType<Guid>(args![0]);
            return expected;
        });
        var handler = new ListOperatorTrackingShuttleTripsHandler(service);

        var result = await handler.Handle(
            new ListOperatorTrackingShuttleTripsQuery(operatorId),
            CancellationToken.None);

        capturedOperatorId.Should().Be(operatorId);
        result.Should().BeSameAs(expected);
    }
}
