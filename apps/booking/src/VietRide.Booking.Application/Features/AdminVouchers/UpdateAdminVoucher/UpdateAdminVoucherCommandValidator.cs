using FluentValidation;

namespace VietRide.Booking.Application.Features.AdminVouchers.UpdateAdminVoucher;

/// <summary>
/// Input-shape validation for <see cref="UpdateAdminVoucherCommand"/>.
/// </summary>
public sealed class UpdateAdminVoucherCommandValidator : AbstractValidator<UpdateAdminVoucherCommand>
{
    private static readonly HashSet<string> ValidServices =
        new(StringComparer.OrdinalIgnoreCase) { "BOOKING", "PARCEL" };

    private static readonly HashSet<string> ValidPaymentMethods =
        new(StringComparer.OrdinalIgnoreCase) { "WALLET", "VNPAY" };

    public UpdateAdminVoucherCommandValidator()
    {
        RuleFor(x => x.VoucherId)
            .NotEmpty();

        RuleFor(x => x.Name)
            .NotEmpty()
            .MaximumLength(120)
            .When(x => x.Name is not null);

        RuleFor(x => x.Value)
            .GreaterThan(0)
            .WithMessage("value must be greater than 0.")
            .When(x => x.Value.HasValue);

        RuleFor(x => x.MinOrderAmount)
            .GreaterThanOrEqualTo(0)
            .WithMessage("minOrderAmount cannot be negative.")
            .When(x => x.MinOrderAmount.HasValue);

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
            .GreaterThan(x => x.ValidFrom!.Value)
            .WithMessage("validUntil must be after validFrom.")
            .When(x => x.ValidFrom.HasValue && x.ValidUntil.HasValue);

        RuleFor(x => x.ApplicableServices)
            .NotEmpty()
            .When(x => x.ApplicableServices is not null)
            .WithMessage("applicableServices must contain at least one service when supplied.");

        RuleForEach(x => x.ApplicableServices)
            .Must(s => !string.IsNullOrWhiteSpace(s) && ValidServices.Contains(s.Trim()))
            .When(x => x.ApplicableServices is not null)
            .WithMessage("applicableServices must contain only BOOKING or PARCEL.");

        RuleForEach(x => x.ApplicablePaymentMethods)
            .Must(s => !string.IsNullOrWhiteSpace(s) && ValidPaymentMethods.Contains(s.Trim()))
            .When(x => x.ApplicablePaymentMethods is not null)
            .WithMessage("applicablePaymentMethods must contain only WALLET or VNPAY.");
    }
}
