using FluentValidation;

namespace VietRide.Trip.Application.Features.FareSurcharges;

public sealed class UpdateFareSurchargeSettingCommandValidator : AbstractValidator<UpdateFareSurchargeSettingCommand>
{
    public UpdateFareSurchargeSettingCommandValidator()
    {
        RuleFor(x => x.OperatorId).NotEmpty();
    }
}
