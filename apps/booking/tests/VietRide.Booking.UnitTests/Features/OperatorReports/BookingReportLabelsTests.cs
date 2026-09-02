using FluentAssertions;
using VietRide.Booking.Application.Features.OperatorReports;
using VietRide.Booking.Domain.Enums;

namespace VietRide.Booking.UnitTests.Features.OperatorReports;

public sealed class BookingReportLabelsTests
{
    [Fact]
    public void EveryBookingStatus_HasVietnameseLabel()
        => Enum.GetValues<BookingStatus>()
            .Select(BookingReportLabels.Status)
            .Should().OnlyContain(label => label != BookingReportLabels.Unknown);

    [Fact]
    public void EveryCancellationReason_HasVietnameseLabel()
        => Enum.GetValues<BookingCancellationReason>()
            .Select(value => BookingReportLabels.CancellationReason(value))
            .Should().OnlyContain(label => label != BookingReportLabels.Unknown);
}
