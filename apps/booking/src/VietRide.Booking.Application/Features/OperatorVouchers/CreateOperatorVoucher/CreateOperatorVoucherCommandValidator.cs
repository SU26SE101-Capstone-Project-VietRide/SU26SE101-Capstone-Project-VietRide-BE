using FluentValidation;
using VietRide.Booking.Domain.Enums;

namespace VietRide.Booking.Application.Features.OperatorVouchers.CreateOperatorVoucher;

/// <summary>
/// Input-shape validation for <see cref="CreateOperatorVoucherCommand"/>.
/// Note: VOUCHER_FORBIDDEN_FUNDING (fundingType != OPERATOR_FUNDED) is a business-rule check
/// handled in the handler rather than here because it results in a 422, not a field validation error.
/// </summary>
public sealed class CreateOperatorVoucherCommandValidator : AbstractValidator<CreateOperatorVoucherCommand>
{
    private static readonly HashSet<string> ValidTypes =
        [.. Enum.GetNames<VoucherType>()];

    public CreateOperatorVoucherCommandValidator()
    {
        RuleFor(x => x.OwnerOperatorId)
            .NotEmpty();

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
    }
}
