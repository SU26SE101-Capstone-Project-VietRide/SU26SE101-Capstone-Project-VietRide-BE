using FluentAssertions;
using VietRide.Parcel.Application.Features.Parcels.CheckIn;
using VietRide.Parcel.Application.Features.Parcels.Create;
using VietRide.Parcel.Application.Features.Parcels.Deliver;

namespace VietRide.Parcel.UnitTests.Features.Parcels;

public sealed class ParcelEvidencePhotoValidatorTests
{
    private const string BucketName = "vietride.appspot.com";
    private static readonly Guid ParcelId = Guid.NewGuid();
    private static readonly Guid TripId = Guid.NewGuid();
    private static readonly Guid AssistantUserId = Guid.NewGuid();
    private static readonly Guid OperatorId = Guid.NewGuid();

    [Fact]
    public void CheckIn_ValidOwnedUrls_AreAccepted()
    {
        var command = CheckInCommand(new[]
        {
            GoogleStorageUrl("check-in-1.jpg"),
            FirebaseDownloadUrl("check-in-2.webp"),
        });

        var result = new CheckInParcelCommandValidator(ImageOptions()).Validate(command);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void Deliver_NoPhotos_IsAccepted()
    {
        var command = DeliverCommand(null);

        var result = new DeliverParcelCommandValidator(ImageOptions()).Validate(command);

        result.IsValid.Should().BeTrue();
    }

    [Fact]
    public void CheckIn_MoreThanThreeUrls_IsRejected()
    {
        var command = CheckInCommand(new[]
        {
            GoogleStorageUrl("1.jpg"),
            GoogleStorageUrl("2.jpg"),
            GoogleStorageUrl("3.jpg"),
            GoogleStorageUrl("4.jpg"),
        });

        var result = new CheckInParcelCommandValidator(ImageOptions()).Validate(command);

        result.Errors.Should().Contain(error => error.PropertyName == "photoUrls");
    }

    [Theory]
    [InlineData("http://storage.googleapis.com/vietride.appspot.com/photo.jpg")]
    [InlineData("https://example.com/photo.jpg")]
    [InlineData("not-a-uri")]
    public void Deliver_UntrustedUrl_IsRejected(string photoUrl)
    {
        var command = DeliverCommand(new[] { photoUrl });

        var result = new DeliverParcelCommandValidator(ImageOptions()).Validate(command);

        result.Errors.Should().ContainSingle(error =>
            error.PropertyName.StartsWith("photoUrls", StringComparison.Ordinal)
            && error.ErrorCode == "VALIDATION_FAILED");
    }

    [Fact]
    public void Deliver_OtherParcelPath_IsRejected()
    {
        var photoUrl =
            $"https://storage.googleapis.com/{BucketName}/parcel-ops/{OperatorId:D}/{AssistantUserId:D}/{Guid.NewGuid():D}/delivery.jpg";

        var result = new DeliverParcelCommandValidator(ImageOptions())
            .Validate(DeliverCommand(new[] { photoUrl }));

        result.Errors.Should().ContainSingle(error =>
            error.PropertyName.StartsWith("photoUrls", StringComparison.Ordinal));
    }

    private static ParcelImageOptions ImageOptions() => new(BucketName);

    private static CheckInParcelCommand CheckInCommand(IReadOnlyCollection<string>? photoUrls)
        => new(
            ParcelId,
            TripId,
            "VR-PCL-20260728-ABCDEFGH",
            photoUrls,
            AssistantUserId,
            OperatorId);

    private static DeliverParcelCommand DeliverCommand(IReadOnlyCollection<string>? photoUrls)
        => new(ParcelId, AssistantUserId, OperatorId, photoUrls);

    private static string GoogleStorageUrl(string fileName)
        => $"https://storage.googleapis.com/{BucketName}/parcel-ops/{OperatorId:D}/{AssistantUserId:D}/{ParcelId:D}/{fileName}";

    private static string FirebaseDownloadUrl(string fileName)
        => $"https://firebasestorage.googleapis.com/v0/b/{BucketName}/o/parcel-ops%2F{OperatorId:D}%2F{AssistantUserId:D}%2F{ParcelId:D}%2F{fileName}?alt=media";
}
