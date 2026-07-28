namespace VietRide.Parcel.Application.Features.Parcels;

internal static class ParcelEvidencePhotoRules
{
    public const int MaximumCount = 3;

    public static string ExpectedObjectPrefix(
        Guid operatorId,
        Guid uploaderUserId,
        Guid parcelId)
        => $"parcel-ops/{operatorId:D}/{uploaderUserId:D}/{parcelId:D}/";

    public static IReadOnlyCollection<string>? Normalize(IReadOnlyCollection<string>? photoUrls)
    {
        if (photoUrls is null || photoUrls.Count == 0)
            return null;

        return photoUrls.Select(url => url.Trim()).ToArray();
    }
}
