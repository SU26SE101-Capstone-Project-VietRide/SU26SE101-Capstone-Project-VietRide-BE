using FluentValidation;
using VietRide.Booking.Domain.Enums;

namespace VietRide.Booking.Application.Features.Bookings.History;

public sealed class GetBookingHistoryQueryValidator : AbstractValidator<GetBookingHistoryQuery>
{
    public GetBookingHistoryQueryValidator()
    {
        RuleFor(query => query.UserId).NotEmpty().WithErrorCode("VALIDATION_ERROR");
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1).WithErrorCode("VALIDATION_ERROR");
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100).WithErrorCode("VALIDATION_ERROR");
        RuleFor(query => query.Status)
            .Must(status => status is null
                || Enum.GetNames<BookingStatus>().Contains(status, StringComparer.OrdinalIgnoreCase))
            .WithMessage("status must be a valid BookingStatus.")
            .WithErrorCode("VALIDATION_ERROR");
        RuleFor(query => query.From)
            .Must(BookingHistoryDateRange.IsOptionalRfc3339)
            .WithMessage("from must be an RFC 3339 timestamp.")
            .WithErrorCode("VALIDATION_ERROR");
        RuleFor(query => query.To)
            .Must(BookingHistoryDateRange.IsOptionalRfc3339)
            .WithMessage("to must be an RFC 3339 timestamp.")
            .WithErrorCode("VALIDATION_ERROR");
        RuleFor(query => query)
            .Must(query => BookingHistoryDateRange.IsOrdered(query.From, query.To))
            .WithName("from")
            .WithMessage("from must be earlier than to.")
            .WithErrorCode("VALIDATION_ERROR");
    }
}
