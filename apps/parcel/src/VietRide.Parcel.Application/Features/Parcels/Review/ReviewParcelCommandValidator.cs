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
        RuleFor(x => x.Reason).NotEmpty().When(x => x.Decision == "REJECTED");
    }
}
