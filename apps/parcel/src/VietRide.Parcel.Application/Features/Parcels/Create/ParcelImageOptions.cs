namespace VietRide.Parcel.Application.Features.Parcels.Create;

public sealed class ParcelImageOptions
{
    public ParcelImageOptions(string? firebaseStorageBucket)
    {
        FirebaseStorageBucket = firebaseStorageBucket?.Trim() ?? string.Empty;
    }

    public string FirebaseStorageBucket { get; }
}
