using FluentValidation;
using VietRide.Parcel.Domain.Enums;

namespace VietRide.Parcel.Application.Features.Parcels.Create;

public sealed class CreateParcelCommandValidator : AbstractValidator<CreateParcelCommand>
{
    private const int MaximumPhotoUrlLength = 2_048;
    private const string FirebaseStorageHost = "firebasestorage.googleapis.com";
    private const string GoogleStorageHost = "storage.googleapis.com";
    private readonly string _firebaseStorageBucket;

    public CreateParcelCommandValidator(ParcelImageOptions imageOptions)
    {
        _firebaseStorageBucket = imageOptions.FirebaseStorageBucket;

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

        RuleFor(x => x.PhotoUrl)
            .Must(BeAllowedPhotoUrl)
            .OverridePropertyName("photoUrl")
            .WithErrorCode("VALIDATION_FAILED")
            .WithMessage("PhotoUrl must be an HTTPS Firebase Storage URL for the configured bucket and at most 2048 characters.")
            .When(x => !string.IsNullOrWhiteSpace(x.PhotoUrl));
    }

    private bool BeAllowedPhotoUrl(string? value)
    {
        var candidate = value?.Trim();
        if (string.IsNullOrEmpty(candidate)
            || candidate.Length > MaximumPhotoUrlLength
            || string.IsNullOrWhiteSpace(_firebaseStorageBucket)
            || !Uri.TryCreate(candidate, UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
            || !uri.IsDefaultPort)
        {
            return false;
        }

        if (string.Equals(uri.Host, FirebaseStorageHost, StringComparison.OrdinalIgnoreCase))
        {
            var prefix = $"/v0/b/{_firebaseStorageBucket}/o/";
            return uri.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal)
                && uri.AbsolutePath.Length > prefix.Length;
        }

        if (string.Equals(uri.Host, GoogleStorageHost, StringComparison.OrdinalIgnoreCase))
        {
            var prefix = $"/{_firebaseStorageBucket}/";
            return uri.AbsolutePath.StartsWith(prefix, StringComparison.Ordinal)
                && uri.AbsolutePath.Length > prefix.Length;
        }

        return false;
    }
}
