using FluentValidation;

namespace VietRide.Parcel.Application.Features.Parcels.ManualConfirmDelivery;

public sealed class ManualConfirmDeliveryCommandValidator : AbstractValidator<ManualConfirmDeliveryCommand>
{
    public ManualConfirmDeliveryCommandValidator()
    {
        RuleFor(x => x.Note).NotEmpty().MaximumLength(500);
    }
}
