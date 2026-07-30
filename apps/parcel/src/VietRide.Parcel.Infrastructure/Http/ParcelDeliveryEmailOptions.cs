namespace VietRide.Parcel.Infrastructure.Http;

public sealed class ParcelDeliveryEmailOptions
{
    public const string SectionName = "ParcelDeliveryEmail";

    public string PublicAppUrl { get; set; } = string.Empty;
}
