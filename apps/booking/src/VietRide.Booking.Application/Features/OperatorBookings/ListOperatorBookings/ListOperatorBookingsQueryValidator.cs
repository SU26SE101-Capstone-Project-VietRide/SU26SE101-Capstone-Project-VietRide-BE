using FluentValidation;
using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Booking.Application.Features.OperatorBookings.ListOperatorBookings;

public sealed class ListOperatorBookingsQueryValidator : AbstractValidator<ListOperatorBookingsQuery>
{
    public ListOperatorBookingsQueryValidator()
    {
        RuleFor(x => x.OperatorId).NotEmpty().WithErrorCode("VALIDATION_ERROR");
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1).WithErrorCode("VALIDATION_ERROR");
        RuleFor(x => x.PageSize).GreaterThanOrEqualTo(1).WithErrorCode("VALIDATION_ERROR");
        RuleFor(x => x.SortDir)
            .Must(value => value.Equals("asc", StringComparison.OrdinalIgnoreCase)
                || value.Equals("desc", StringComparison.OrdinalIgnoreCase))
            .WithErrorCode("VALIDATION_ERROR");
        RuleFor(x => x.Status)
            .Must(BeValidStatuses)
            .WithErrorCode("VALIDATION_ERROR");
        RuleFor(x => x.PassengerPhone)
            .Must(BeValidPhone)
            .WithErrorCode("VALIDATION_ERROR");
        RuleFor(x => x.BookingCode)
            .Must(value => value is null || value.Trim() is { Length: > 0 and <= 30 })
            .WithErrorCode("VALIDATION_ERROR");
    }

    private static bool BeValidStatuses(string? value)
        => value is null || (value.Split(',', StringSplitOptions.None) is { Length: > 0 } values
            && values.All(status => !string.IsNullOrWhiteSpace(status)
                && Enum.GetNames<BookingStatus>().Contains(status.Trim(), StringComparer.OrdinalIgnoreCase)));

    private static bool BeValidPhone(string? value)
    {
        if (value is null)
            return true;

        try
        {
            _ = PhoneNumber.Normalize(value);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }
}
