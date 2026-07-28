using FluentValidation;
using VietRide.Parcel.Application.Features.Parcels.Create;
using VietRide.Shared.Application.Security;

namespace VietRide.Parcel.Application.Features.Parcels.Deliver;

public sealed class DeliverParcelCommandValidator : AbstractValidator<DeliverParcelCommand>
{
    public DeliverParcelCommandValidator(ParcelImageOptions imageOptions)
    {
        var firebaseUrls = new FirebaseStorageImageUrlValidator(imageOptions.FirebaseStorageBucket);

        RuleFor(x => x.ParcelId).NotEmpty();
        RuleFor(x => x.ActorUserId).NotEmpty();
        RuleFor(x => x.OperatorId).NotEmpty();
        RuleFor(x => x.PhotoUrls)
            .Must(photoUrls => photoUrls is null
                || photoUrls.Count <= ParcelEvidencePhotoRules.MaximumCount)
            .OverridePropertyName("photoUrls")
            .WithMessage($"At most {ParcelEvidencePhotoRules.MaximumCount} photo URLs are allowed.");
        RuleForEach(x => x.PhotoUrls)
            .Must((command, photoUrl) => firebaseUrls.IsValidOwnedImageUrl(
                photoUrl,
                ParcelEvidencePhotoRules.ExpectedObjectPrefix(
                    command.OperatorId,
                    command.ActorUserId,
                    command.ParcelId)))
            .OverridePropertyName("photoUrls")
            .WithErrorCode("VALIDATION_FAILED")
            .WithMessage("PhotoUrls must be owned Firebase Parcel evidence URLs.");
    }
}
