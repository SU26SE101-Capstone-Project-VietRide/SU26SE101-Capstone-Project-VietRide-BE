namespace VietRide.Shared.Application.Security;

public sealed class FirebaseStorageImageUrlValidator : IFirebaseStorageImageUrlValidator
{
    private const string FirebaseDownloadHost = "firebasestorage.googleapis.com";
    private const string GoogleStorageHost = "storage.googleapis.com";
    private readonly string _bucketName;

    public FirebaseStorageImageUrlValidator(string? bucketName)
    {
        _bucketName = NormalizeBucket(bucketName);
    }

    public bool IsValidOwnedImageUrl(string? url, string expectedObjectPrefix)
    {
        if (string.IsNullOrWhiteSpace(url)
            || string.IsNullOrWhiteSpace(expectedObjectPrefix)
            || string.IsNullOrWhiteSpace(_bucketName)
            || url.Length > 2048
            || !Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri)
            || !string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!TryGetBucketAndObjectPath(uri, out var bucket, out var objectPath)
            || !string.Equals(bucket, _bucketName, StringComparison.OrdinalIgnoreCase)
            || objectPath.Contains("..", StringComparison.Ordinal))
        {
            return false;
        }

        return objectPath.StartsWith(expectedObjectPrefix, StringComparison.Ordinal)
            && objectPath.Length > expectedObjectPrefix.Length;
    }

    private static bool TryGetBucketAndObjectPath(
        Uri uri,
        out string bucket,
        out string objectPath)
    {
        bucket = string.Empty;
        objectPath = string.Empty;
        var segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);

        if (string.Equals(uri.Host, FirebaseDownloadHost, StringComparison.OrdinalIgnoreCase)
            && segments.Length >= 5
            && string.Equals(segments[0], "v0", StringComparison.Ordinal)
            && string.Equals(segments[1], "b", StringComparison.Ordinal)
            && string.Equals(segments[3], "o", StringComparison.Ordinal))
        {
            bucket = Uri.UnescapeDataString(segments[2]);
            objectPath = Uri.UnescapeDataString(string.Join('/', segments[4..]));
            return true;
        }

        if (string.Equals(uri.Host, GoogleStorageHost, StringComparison.OrdinalIgnoreCase)
            && segments.Length >= 2)
        {
            bucket = Uri.UnescapeDataString(segments[0]);
            objectPath = Uri.UnescapeDataString(string.Join('/', segments[1..]));
            return true;
        }

        return false;
    }

    private static string NormalizeBucket(string? bucketName)
    {
        var normalized = bucketName?.Trim() ?? string.Empty;
        return normalized.StartsWith("gs://", StringComparison.OrdinalIgnoreCase)
            ? normalized[5..].TrimEnd('/')
            : normalized.TrimEnd('/');
    }
}
