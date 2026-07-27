using FluentValidation;

namespace VietRide.Parcel.Application.Features.Parcels.Reweigh;

public sealed class ReweighParcelCommandValidator : AbstractValidator<ReweighParcelCommand>
{
    public ReweighParcelCommandValidator()
    {
        RuleFor(x => x.ParcelId).NotEmpty();
        RuleFor(x => x.OperatorId).NotEmpty();
        RuleFor(x => x.ActualLengthCm).GreaterThan(0);
        RuleFor(x => x.ActualWidthCm).GreaterThan(0);
        RuleFor(x => x.ActualHeightCm).GreaterThan(0);
        RuleFor(x => x.ActualWeightKg).GreaterThan(0);
    }
}
