using FluentValidation;
using VietRide.Booking.Domain.Enums;

namespace VietRide.Booking.Application.Features.Vouchers.ListVouchers;

/// <summary>
/// Validates sortBy whitelist and optional fundingType for voucher list endpoints (BSOT §5.8).
/// Non-whitelisted sortBy → 422 INVALID_SORT_FIELD.
/// </summary>
public sealed class ListVouchersQueryValidator : AbstractValidator<ListVouchersQuery>
{
    private static readonly HashSet<string> AllowedSortFields =
    [
        "createdAt",
        "validFrom",
        "validUntil",
        "code",
        "name",
        "isActive",
    ];

    private static readonly HashSet<string> ValidFundingTypes =
        [.. Enum.GetNames<VoucherFundingType>()];

    public ListVouchersQueryValidator()
    {
        RuleFor(x => x.Options.SortBy)
            .Must(sortBy => sortBy is null || AllowedSortFields.Contains(sortBy))
            .WithErrorCode("INVALID_SORT_FIELD")
            .WithMessage(
                $"sortBy must be one of: {string.Join(", ", AllowedSortFields)}.");

        RuleFor(x => x.FundingType)
            .Must(f => f is null || ValidFundingTypes.Contains(f, StringComparer.OrdinalIgnoreCase))
            .WithErrorCode("VALIDATION_ERROR")
            .WithMessage($"fundingType must be one of: {string.Join(", ", ValidFundingTypes)}.");
        RuleFor(x => x.Search).MaximumLength(255);
        RuleFor(x => x.Service)
            .Must(value => string.IsNullOrWhiteSpace(value)
                || value.Equals("BOOKING", StringComparison.OrdinalIgnoreCase)
                || value.Equals("PARCEL", StringComparison.OrdinalIgnoreCase))
            .WithErrorCode("VALIDATION_ERROR")
            .WithMessage("service must be BOOKING or PARCEL.");
    }
}
