using FluentValidation;
using VietRide.Identity.Domain.Enums;

namespace VietRide.Identity.Application.Features.Devices.RegisterDeviceToken;

public sealed class RegisterDeviceTokenCommandValidator : AbstractValidator<RegisterDeviceTokenCommand>
{
    public RegisterDeviceTokenCommandValidator()
    {
        RuleFor(x => x.UserId)
            .NotEmpty();

        RuleFor(x => x.FcmToken)
            .NotEmpty()
            .MaximumLength(500);

        RuleFor(x => x.Platform)
            .NotEmpty()
            .Must(BeValidPlatform)
            .WithMessage("Platform must be one of IOS, ANDROID, or WEB.");
    }

    private static bool BeValidPlatform(string platform)
        => !string.IsNullOrWhiteSpace(platform)
            && Enum.TryParse<DevicePlatform>(platform, ignoreCase: true, out var parsed)
            && Enum.IsDefined(parsed)
            && !int.TryParse(platform, out _);
}
