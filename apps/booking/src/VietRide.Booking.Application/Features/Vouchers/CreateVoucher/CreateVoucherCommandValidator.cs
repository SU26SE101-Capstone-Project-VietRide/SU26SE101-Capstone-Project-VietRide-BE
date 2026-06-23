using FluentValidation;
using VietRide.Booking.Domain.Enums;

namespace VietRide.Booking.Application.Features.Vouchers.CreateVoucher;

/// <summary>
/// Input-shape and field validation for <see cref="CreateVoucherCommand"/>.
/// Business-rule: OPERATOR_FUNDED requires non-null non-empty applicableOperatorIds (Q3 RESOLVED).
/// </summary>
public sealed class CreateVoucherCommandValidator : AbstractValidator<CreateVoucherCommand>
{
    private static readonly HashSet<string> ValidTypes =
        [.. Enum.GetNames<VoucherType>()];

    private static readonly HashSet<string> ValidFundingTypes =
        [.. Enum.GetNames<VoucherFundingType>()];

    public CreateVoucherCommandValidator()
    {
        RuleFor(x => x.CreatedByUserId)
            .NotEmpty();

        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(120);

        // code is optional (null = auto-generate); if supplied must be non-whitespace ≤50 chars.
        RuleFor(x => x.Code)
            .MaximumLength(50)
            .When(x => x.Code is not null)
            .WithMessage("code must not exceed 50 characters.");

        RuleFor(x => x.Type)
            .NotEmpty()
            .Must(t => ValidTypes.Contains(t, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"type must be one of: {string.Join(", ", ValidTypes)}.");

        RuleFor(x => x.FundingType)
            .NotEmpty()
            .Must(f => ValidFundingTypes.Contains(f, StringComparer.OrdinalIgnoreCase))
            .WithMessage($"fundingType must be one of: {string.Join(", ", ValidFundingTypes)}.");

        RuleFor(x => x.Value)
            .GreaterThan(0)
            .WithMessage("value must be greater than 0.");

        RuleFor(x => x.MinOrderAmount)
            .GreaterThanOrEqualTo(0)
            .WithMessage("minOrderAmount cannot be negative.");

        RuleFor(x => x.MaxDiscountAmount)
            .GreaterThan(0)
            .When(x => x.MaxDiscountAmount.HasValue)
            .WithMessage("maxDiscountAmount must be greater than 0 when supplied.");

        RuleFor(x => x.TotalUsageLimit)
            .GreaterThan(0)
            .When(x => x.TotalUsageLimit.HasValue)
            .WithMessage("totalUsageLimit must be greater than 0 when supplied.");

        RuleFor(x => x.PerUserLimit)
            .GreaterThan(0)
            .When(x => x.PerUserLimit.HasValue)
            .WithMessage("perUserLimit must be greater than 0 when supplied.");

        RuleFor(x => x.ValidUntil)
            .GreaterThan(x => x.ValidFrom)
            .WithMessage("validUntil must be after validFrom.");

        // Q3 RESOLVED: OPERATOR_FUNDED requires a non-null, non-empty applicableOperatorIds list.
        RuleFor(x => x.ApplicableOperatorIds)
            .NotNull()
            .NotEmpty()
            .When(x => string.Equals(x.FundingType, "OPERATOR_FUNDED", StringComparison.OrdinalIgnoreCase))
            .WithErrorCode("VALIDATION_ERROR")
            .WithMessage("applicableOperatorIds is required for OPERATOR_FUNDED vouchers.");
    }
}
