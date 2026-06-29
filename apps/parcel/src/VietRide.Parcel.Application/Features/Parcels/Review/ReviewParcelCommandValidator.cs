using FluentValidation;

namespace VietRide.Parcel.Application.Features.Parcels.Review;

public sealed class ReviewParcelCommandValidator : AbstractValidator<ReviewParcelCommand>
{
    public ReviewParcelCommandValidator()
    {
        RuleFor(x => x.ParcelId).NotEmpty();
        RuleFor(x => x.OperatorId).NotEmpty();
        RuleFor(x => x.ReviewedByUserId).NotEmpty();
        RuleFor(x => x.Decision).Must(d => d is "APPROVED" or "REJECTED")
            .WithMessage("Decision must be APPROVED or REJECTED.");
        RuleFor(x => x.PaymentMethod)
            .NotEmpty()
            .When(x => x.Decision == "APPROVED");
        RuleFor(x => x.PaymentMethod)
            .Must(m => m is "WALLET" or "VNPAY")
            .When(x => x.Decision == "APPROVED")
            .WithMessage("PaymentMethod must be WALLET or VNPAY.");
        RuleFor(x => x.DepositAmount).GreaterThan(0).When(x => x.Decision == "APPROVED");
        RuleFor(x => x.Reason).NotEmpty().When(x => x.Decision == "REJECTED");
    }
}
