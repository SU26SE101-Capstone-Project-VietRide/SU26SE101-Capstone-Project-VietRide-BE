using FluentValidation;

namespace VietRide.Parcel.Application.Features.Parcels.CheckIn;

public sealed class CheckInParcelCommandValidator : AbstractValidator<CheckInParcelCommand>
{
    public CheckInParcelCommandValidator()
    {
        RuleFor(x => x.ParcelId).NotEmpty();
        RuleFor(x => x.TripId).NotEmpty();
        RuleFor(x => x.ParcelCode).NotEmpty();
        RuleFor(x => x.AssistantUserId).NotEmpty();
        RuleFor(x => x.OperatorId).NotEmpty();
    }
}
