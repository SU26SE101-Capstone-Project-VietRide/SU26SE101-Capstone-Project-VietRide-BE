using FluentValidation;

namespace VietRide.Parcel.Application.Features.Parcels.OperationalRecovery;

public sealed class UpdateParcelStatusCommandValidator : AbstractValidator<UpdateParcelStatusCommand>
{
    public UpdateParcelStatusCommandValidator()
    {
        RuleFor(x => x.Status).NotEmpty();
        RuleFor(x => x.Reason).NotEmpty();
    }
}
