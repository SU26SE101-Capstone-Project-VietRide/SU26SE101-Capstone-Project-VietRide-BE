using FluentAssertions;
using VietRide.Booking.Application.Features.OperatorBookings.GetOperatorBookingDetail;

namespace VietRide.Booking.UnitTests.Features.OperatorBookings;

public sealed class GetOperatorBookingDetailQueryValidatorTests
{
    [Fact]
    public void Validate_EmptyIds_IsInvalid()
    {
        var query = new GetOperatorBookingDetailQuery(Guid.Empty, Guid.Empty);
        var result = new GetOperatorBookingDetailQueryValidator().Validate(query);

        result.IsValid.Should().BeFalse();
        result.Errors.Select(error => error.PropertyName).Should().BeEquivalentTo(["BookingId", "OperatorId"]);
    }

    [Fact]
    public void Validate_NonEmptyIds_IsValid()
        => new GetOperatorBookingDetailQueryValidator().Validate(
                new GetOperatorBookingDetailQuery(Guid.NewGuid(), Guid.NewGuid()))
            .IsValid.Should().BeTrue();
}
