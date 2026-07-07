using FluentValidation;

namespace VietRide.Parcel.Application.Features.Parcels.Received;

public sealed class GetReceivedParcelsQueryValidator : AbstractValidator<GetReceivedParcelsQuery>
{
    public GetReceivedParcelsQueryValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100);
    }
}
