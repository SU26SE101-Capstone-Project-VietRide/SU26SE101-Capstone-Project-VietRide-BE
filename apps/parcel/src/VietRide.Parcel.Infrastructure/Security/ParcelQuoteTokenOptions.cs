namespace VietRide.Parcel.Infrastructure.Security;

public sealed record ParcelQuoteTokenOptions(string Secret, int TtlSeconds);
