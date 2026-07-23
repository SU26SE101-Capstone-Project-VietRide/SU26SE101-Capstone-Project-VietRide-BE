using FluentValidation;
using VietRide.Shared.Application.Security;

namespace VietRide.Identity.Application.Features.Users.UpdateAvatar;

public sealed class UpdateAvatarCommandValidator : AbstractValidator<UpdateAvatarCommand>
{
    public UpdateAvatarCommandValidator(IFirebaseStorageImageUrlValidator firebaseUrls)
    {
        RuleFor(command => command.UserId).NotEmpty();
        RuleFor(command => command.AvatarUrl)
            .MaximumLength(2048)
            .Must((command, avatarUrl) => avatarUrl is null
                || firebaseUrls.IsValidOwnedImageUrl(
                    avatarUrl,
                    $"avatars/{command.UserId:D}/"))
            .WithMessage("AvatarUrl must be an owned Firebase user avatar URL.");
    }
}
