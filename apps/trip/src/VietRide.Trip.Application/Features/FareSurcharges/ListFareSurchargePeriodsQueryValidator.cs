using FluentValidation;

namespace VietRide.Trip.Application.Features.FareSurcharges;

public sealed class ListFareSurchargePeriodsQueryValidator : AbstractValidator<ListFareSurchargePeriodsQuery>
{
    public ListFareSurchargePeriodsQueryValidator()
    {
        RuleFor(x => x.OperatorId).NotEmpty();
        RuleFor(x => x.Page).GreaterThanOrEqualTo(1).When(x => x.Page.HasValue);
        RuleFor(x => x.PageSize).InclusiveBetween(1, 100).When(x => x.PageSize.HasValue);
    }
}
