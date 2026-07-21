using FluentValidation;
using VietRide.Parcel.Application.Features.History;
using VietRide.Parcel.Domain.Enums;

namespace VietRide.Parcel.Application.Features.PassengerHistory;

public sealed class GetPassengerHistoryQueryValidator : AbstractValidator<GetPassengerHistoryQuery>
{
    private static readonly HashSet<string> TicketStatuses = new(
        [
            "PENDING_PAYMENT",
            "CONFIRMED",
            "COMPLETED",
            "EXPIRED",
            "CANCELLED",
            "NO_SHOW",
            "PARTIAL_NO_SHOW",
            "REFUNDED",
            "DISRUPTED",
        ],
        StringComparer.OrdinalIgnoreCase);

    public GetPassengerHistoryQueryValidator()
    {
        RuleFor(query => query.UserId).NotEmpty().WithErrorCode("VALIDATION_ERROR");
        RuleFor(query => query.Type)
            .Must(type => type.Equals("TICKET", StringComparison.OrdinalIgnoreCase)
                || type.Equals("PARCEL", StringComparison.OrdinalIgnoreCase))
            .WithMessage("type must be TICKET or PARCEL.")
            .WithErrorCode("VALIDATION_ERROR");
        RuleFor(query => query.Page).GreaterThanOrEqualTo(1).WithErrorCode("VALIDATION_ERROR");
        RuleFor(query => query.PageSize).InclusiveBetween(1, 100).WithErrorCode("VALIDATION_ERROR");
        RuleFor(query => query.Status)
            .Must((query, status) => IsValidStatus(query.Type, status))
            .WithMessage("status is invalid for the selected history type.")
            .WithErrorCode("VALIDATION_ERROR");
        RuleFor(query => query.From)
            .Must(ParcelHistoryDateRange.IsOptionalRfc3339)
            .WithMessage("from must be an RFC 3339 timestamp.")
            .WithErrorCode("VALIDATION_ERROR");
        RuleFor(query => query.To)
            .Must(ParcelHistoryDateRange.IsOptionalRfc3339)
            .WithMessage("to must be an RFC 3339 timestamp.")
            .WithErrorCode("VALIDATION_ERROR");
        RuleFor(query => query)
            .Must(query => ParcelHistoryDateRange.IsOrdered(query.From, query.To))
            .WithName("from")
            .WithMessage("from must be earlier than to.")
            .WithErrorCode("VALIDATION_ERROR");
    }

    private static bool IsValidStatus(string type, string? status)
    {
        if (status is null)
            return true;
        if (type.Equals("TICKET", StringComparison.OrdinalIgnoreCase))
            return TicketStatuses.Contains(status);
        if (type.Equals("PARCEL", StringComparison.OrdinalIgnoreCase))
            return Enum.GetNames<ParcelStatus>().Contains(status, StringComparer.OrdinalIgnoreCase);
        return true;
    }
}
