using FluentValidation;

namespace VietRide.Trip.Application.Features.FareSurcharges;

public sealed class UpdateFareSurchargePeriodCommandValidator : AbstractValidator<UpdateFareSurchargePeriodCommand>
{
    public UpdateFareSurchargePeriodCommandValidator()
    {
        RuleFor(x => x.OperatorId).NotEmpty();
        RuleFor(x => x.PeriodId).NotEmpty();
        RuleFor(x => x.Name).NotEmpty().MaximumLength(120).When(x => x.Name is not null);
        RuleFor(x => x.SurchargePercent).InclusiveBetween(1, 100).When(x => x.SurchargePercent.HasValue);
        RuleFor(x => x)
            .Must(x => x.Name is not null
                || x.StartDate.HasValue
                || x.EndDate.HasValue
                || x.SurchargePercent.HasValue
                || x.IsActive.HasValue)
            .WithMessage("At least one field must be supplied.");
    }
}
