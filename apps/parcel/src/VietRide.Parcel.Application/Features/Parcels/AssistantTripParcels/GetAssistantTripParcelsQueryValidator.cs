using FluentValidation;

namespace VietRide.Parcel.Application.Features.Parcels.AssistantTripParcels;

public sealed class GetAssistantTripParcelsQueryValidator : AbstractValidator<GetAssistantTripParcelsQuery>
{
    public GetAssistantTripParcelsQueryValidator()
    {
        RuleFor(x => x.TripId).NotEmpty();
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.OperatorId).NotEmpty();
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}
