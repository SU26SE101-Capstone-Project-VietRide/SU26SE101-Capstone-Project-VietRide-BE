using FluentValidation;

namespace VietRide.Booking.Application.Features.OperatorBookings.GetOperatorBookingDetail;

public sealed class GetOperatorBookingDetailQueryValidator : AbstractValidator<GetOperatorBookingDetailQuery>
{
    public GetOperatorBookingDetailQueryValidator()
    {
        RuleFor(query => query.BookingId).NotEmpty();
        RuleFor(query => query.OperatorId).NotEmpty();
    }
}
