using FluentValidation;

namespace VietRide.Parcel.Application.Features.Reliability.Reconciliation;

public sealed class GetParcelTripCompletionClearanceQueryValidator
    : AbstractValidator<GetParcelTripCompletionClearanceQuery>
{
    public GetParcelTripCompletionClearanceQueryValidator()
    {
        RuleFor(x => x.TripId).NotEmpty();
        RuleFor(x => x.OperatorId).NotEmpty();
    }
}
