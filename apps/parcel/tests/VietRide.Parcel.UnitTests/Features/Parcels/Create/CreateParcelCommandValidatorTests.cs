using FluentAssertions;
using VietRide.Parcel.Application.Features.Parcels.Create;

namespace VietRide.Parcel.UnitTests.Features.Parcels.Create;

public sealed class CreateParcelCommandValidatorTests
{
    private const string BucketName = "vietride.appspot.com";

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("https://firebasestorage.googleapis.com/v0/b/vietride.appspot.com/o/parcels%2Fphoto.jpg?alt=media")]
    [InlineData("https://storage.googleapis.com/vietride.appspot.com/parcels/photo.webp")]
    [InlineData("  https://storage.googleapis.com/vietride.appspot.com/parcels/photo.webp  ")]
    public void Validate_AcceptsOptionalOrConfiguredFirebasePhotoUrl(string? photoUrl)
    {
        var result = CreateValidator().Validate(CreateCommand(photoUrl));

        result.Errors.Should().NotContain(error => error.PropertyName == "photoUrl");
    }

    [Theory]
    [InlineData("http://storage.googleapis.com/vietride.appspot.com/parcels/photo.jpg")]
    [InlineData("/parcels/photo.jpg")]
    [InlineData("not-a-uri")]
    [InlineData("https://example.com/photo.jpg")]
    [InlineData("https://storage.googleapis.com:8443/vietride.appspot.com/parcels/photo.jpg")]
    [InlineData("https://storage.googleapis.com/other-bucket/parcels/photo.jpg")]
    [InlineData("https://firebasestorage.googleapis.com/v0/b/other-bucket/o/parcels%2Fphoto.jpg")]
    public void Validate_RejectsUntrustedPhotoUrl(string photoUrl)
    {
        var result = CreateValidator().Validate(CreateCommand(photoUrl));

        result.Errors.Should().ContainSingle(error =>
            error.PropertyName == "photoUrl"
            && error.ErrorCode == "VALIDATION_FAILED");
    }

    [Fact]
    public void Validate_RejectsPhotoUrlLongerThanMaximum()
    {
        var photoUrl = $"https://storage.googleapis.com/{BucketName}/{new string('a', 2_048)}";

        var result = CreateValidator().Validate(CreateCommand(photoUrl));

        result.Errors.Should().ContainSingle(error =>
            error.PropertyName == "photoUrl"
            && error.ErrorCode == "VALIDATION_FAILED");
    }

    private static CreateParcelCommandValidator CreateValidator()
        => new(new ParcelImageOptions(BucketName));

    private static CreateParcelCommand CreateCommand(string? photoUrl)
        => new(
            Guid.NewGuid(),
            null,
            "Recipient",
            "0900000000",
            null,
            Guid.NewGuid(),
            null,
            null,
            "Parcel",
            null,
            photoUrl,
            "SMALL",
            10m,
            10m,
            10m,
            1m,
            "TERMINAL_PICKUP",
            "VNPAY");
}
