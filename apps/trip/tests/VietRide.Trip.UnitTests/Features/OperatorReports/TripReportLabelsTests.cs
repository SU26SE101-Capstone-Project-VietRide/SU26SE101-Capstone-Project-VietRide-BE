using FluentAssertions;
using VietRide.Trip.Application.Features.OperatorReports;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.UnitTests.Features.OperatorReports;

public sealed class TripReportLabelsTests
{
    [Fact]
    public void EveryTripStatus_HasVietnameseLabel()
        => Enum.GetValues<TripStatus>()
            .Select(TripReportLabels.Status)
            .Should().OnlyContain(label => label != TripReportLabels.Unknown);
}
