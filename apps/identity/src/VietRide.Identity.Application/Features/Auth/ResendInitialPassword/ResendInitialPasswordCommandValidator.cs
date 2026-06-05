using FluentValidation;

namespace VietRide.Identity.Application.Features.Auth.ResendInitialPassword;

public sealed class ResendInitialPasswordCommandValidator : AbstractValidator<ResendInitialPasswordCommand>
{
    public ResendInitialPasswordCommandValidator()
    {
        RuleFor(x => x.UserId).NotEmpty();
    }
}
