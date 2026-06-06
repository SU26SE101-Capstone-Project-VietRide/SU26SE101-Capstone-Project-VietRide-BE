using FluentValidation;

namespace VietRide.Identity.Application.Features.Devices.RemoveDeviceToken;

public sealed class RemoveDeviceTokenCommandValidator : AbstractValidator<RemoveDeviceTokenCommand>
{
    public RemoveDeviceTokenCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty();

        RuleFor(x => x.FcmToken)
            .NotEmpty()
            .MaximumLength(500)
            .OverridePropertyName("fcmToken");
    }
}
