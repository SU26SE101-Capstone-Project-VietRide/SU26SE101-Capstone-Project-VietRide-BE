using FluentValidation;

namespace VietRide.Trip.Application.Features.FareSurcharges;

public sealed class DeleteFareSurchargePeriodCommandValidator : AbstractValidator<DeleteFareSurchargePeriodCommand>
{
    public DeleteFareSurchargePeriodCommandValidator()
    {
        RuleFor(x => x.OperatorId).NotEmpty();
        RuleFor(x => x.PeriodId).NotEmpty();
    }
}
