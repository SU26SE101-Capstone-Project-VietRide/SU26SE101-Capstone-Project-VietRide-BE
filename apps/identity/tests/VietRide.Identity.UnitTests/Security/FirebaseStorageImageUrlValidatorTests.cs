using FluentAssertions;
using VietRide.Shared.Application.Security;

namespace VietRide.Identity.UnitTests.Security;

public sealed class FirebaseStorageImageUrlValidatorTests
{
    private const string Bucket = "vietride-test.firebasestorage.app";
    private readonly FirebaseStorageImageUrlValidator _validator = new(Bucket);

    [Fact]
    public void FirebaseDownloadUrl_WithOwnedPrefix_IsAccepted()
    {
        var userId = Guid.NewGuid();
        var url =
            $"https://firebasestorage.googleapis.com/v0/b/{Bucket}/o/avatars%2F{userId:D}%2Favatar.webp?alt=media&token=test";

        _validator.IsValidOwnedImageUrl(url, $"avatars/{userId:D}/").Should().BeTrue();
    }

    [Theory]
    [InlineData("https://example.com/avatar.webp")]
    [InlineData("http://firebasestorage.googleapis.com/v0/b/vietride-test.firebasestorage.app/o/avatars%2Fuser%2Favatar.webp")]
    [InlineData("https://firebasestorage.googleapis.com/v0/b/other.firebasestorage.app/o/avatars%2Fuser%2Favatar.webp")]
    public void ForeignHostProtocolOrBucket_IsRejected(string url)
    {
        _validator.IsValidOwnedImageUrl(url, "avatars/user/").Should().BeFalse();
    }

    [Fact]
    public void CrossOwnerObjectPath_IsRejected()
    {
        var ownerId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var url =
            $"https://firebasestorage.googleapis.com/v0/b/{Bucket}/o/parcels%2F{otherId:D}%2Fparcel.jpg?alt=media";

        _validator.IsValidOwnedImageUrl(url, $"parcels/{ownerId:D}/").Should().BeFalse();
    }
}
