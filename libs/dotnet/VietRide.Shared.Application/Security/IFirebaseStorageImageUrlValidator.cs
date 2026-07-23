namespace VietRide.Shared.Application.Security;

public interface IFirebaseStorageImageUrlValidator
{
    bool IsValidOwnedImageUrl(string? url, string expectedObjectPrefix);
}
