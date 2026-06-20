using FluentValidation;

namespace VietRide.Booking.Application.Features.OperatorVouchers.UpdateOperatorVoucher;

/// <summary>
/// Input-shape validation for <see cref="UpdateOperatorVoucherCommand"/>.
/// All mutable fields are optional (null = keep current value), so rules only fire
/// when the field is actually provided (non-null).
/// Freeze-on-first-use business rules (VOUCHER_LOCKED) are enforced in the handler
/// after consulting the DB (CountUsages) — not here.
/// </summary>
public sealed class UpdateOperatorVoucherCommandValidator : AbstractValidator<UpdateOperatorVoucherCommand>
{
    public UpdateOperatorVoucherCommandValidator()
    {
        RuleFor(x => x.VoucherId)
            .NotEmpty();

        RuleFor(x => x.CallerOperatorId)
            .NotEmpty();

        // Name: only validate when provided (non-null).
        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(120)
            .When(x => x.Name is not null);

        // Value: only validate when provided (non-null).
        RuleFor(x => x.Value)
            .GreaterThan(0)
            .WithMessage("value must be greater than 0.")
            .When(x => x.Value.HasValue);

        // MinOrderAmount: only validate when provided (non-null).
        RuleFor(x => x.MinOrderAmount)
            .GreaterThanOrEqualTo(0)
            .WithMessage("minOrderAmount cannot be negative.")
            .When(x => x.MinOrderAmount.HasValue);

        // MaxDiscountAmount: only validate when provided (non-null).
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

        // ValidUntil > ValidFrom: only validate if both are provided.
        RuleFor(x => x.ValidUntil)
            .GreaterThan(x => x.ValidFrom!.Value)
            .WithMessage("validUntil must be after validFrom.")
            .When(x => x.ValidFrom.HasValue && x.ValidUntil.HasValue);
    }
}
