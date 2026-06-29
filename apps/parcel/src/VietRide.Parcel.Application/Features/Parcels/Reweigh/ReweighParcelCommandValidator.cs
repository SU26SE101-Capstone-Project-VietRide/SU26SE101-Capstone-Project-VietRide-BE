using FluentValidation;

namespace VietRide.Parcel.Application.Features.Parcels.Reweigh;

public sealed class ReweighParcelCommandValidator : AbstractValidator<ReweighParcelCommand>
{
    public ReweighParcelCommandValidator()
    {
        RuleFor(x => x.ParcelId).NotEmpty();
        RuleFor(x => x.OperatorId).NotEmpty();
        RuleFor(x => x.ActualWeightKg).GreaterThan(0);
        RuleFor(x => x.ActualSizeCategory).NotEmpty();
        RuleFor(x => x.PaymentMethod).Must(m => m is "WALLET" or "VNPAY")
            .WithMessage("PaymentMethod must be WALLET or VNPAY.");
    }
}
