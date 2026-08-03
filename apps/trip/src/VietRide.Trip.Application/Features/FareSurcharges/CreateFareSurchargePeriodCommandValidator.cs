using FluentValidation;

namespace VietRide.Trip.Application.Features.FareSurcharges;

public sealed class CreateFareSurchargePeriodCommandValidator : AbstractValidator<CreateFareSurchargePeriodCommand>
{
    public CreateFareSurchargePeriodCommandValidator()
    {
        RuleFor(x => x.OperatorId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120);
        RuleFor(x => x.StartDate).NotEmpty();
        RuleFor(x => x.EndDate).GreaterThanOrEqualTo(x => x.StartDate);
        RuleFor(x => x.SurchargePercent).InclusiveBetween(1, 100);
    }
}
