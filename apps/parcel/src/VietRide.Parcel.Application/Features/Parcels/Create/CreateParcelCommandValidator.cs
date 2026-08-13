using FluentValidation;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Security;

namespace VietRide.Parcel.Application.Features.Parcels.Create;

public sealed class CreateParcelCommandValidator : AbstractValidator<CreateParcelCommand>
{
    public CreateParcelCommandValidator(ParcelImageOptions imageOptions)
    {
        var firebaseUrls = new FirebaseStorageImageUrlValidator(imageOptions.FirebaseStorageBucket);

        RuleFor(x => x.SenderUserId)
            .NotEmpty();

        RuleFor(x => x.RecipientName)
            .NotEmpty()
            .MaximumLength(255);

        RuleFor(x => x.RecipientPhone)
            .NotEmpty()
            .MaximumLength(20);

        RuleFor(x => x.RecipientEmail)
            .MaximumLength(255)
            .EmailAddress()
            .When(x => x.RecipientEmail is not null);

        RuleFor(x => x.TripId)
            .NotEmpty();

        RuleFor(x => x.SizeCategory)
            .NotEmpty()
            .Must(v => Enum.TryParse<ParcelSizeCategory>(v, ignoreCase: true, out _))
            .WithMessage("SizeCategory must be a valid ParcelSizeCategory value.");

        RuleFor(x => x.EstimatedWeightKg)
            .GreaterThan(0);

        RuleFor(x => x.LengthCm)
            .GreaterThan(0);
        RuleFor(x => x.WidthCm)
            .GreaterThan(0);
        RuleFor(x => x.HeightCm)
            .GreaterThan(0);

        RuleFor(x => x.DeliveryMethod)
            .NotEmpty()
            .Must(v => v == "TERMINAL_PICKUP")
            .WithMessage("Only TERMINAL_PICKUP delivery method is supported.");

        RuleFor(x => x.PaymentMethod)
            .NotEmpty()
            .Must(v => v is "VNPAY" or "WALLET")
            .WithMessage("PaymentMethod must be either 'VNPAY' or 'WALLET'.");

        RuleFor(x => x.Description)
            .MaximumLength(2000)
            .When(x => x.Description is not null);

        RuleFor(x => x.QuoteToken)
            .MaximumLength(16_384)
            .When(x => x.QuoteToken is not null);

        RuleFor(x => x)
            .Must(x => string.IsNullOrWhiteSpace(x.PhotoUrl) || firebaseUrls.IsValidOwnedImageUrl(
                x.PhotoUrl,
                $"parcels/{x.SenderUserId:D}/"))
            .OverridePropertyName("photoUrl")
            .WithErrorCode("VALIDATION_FAILED")
            .WithMessage("PhotoUrl must be an owned Firebase parcel photo URL.");
    }
}
