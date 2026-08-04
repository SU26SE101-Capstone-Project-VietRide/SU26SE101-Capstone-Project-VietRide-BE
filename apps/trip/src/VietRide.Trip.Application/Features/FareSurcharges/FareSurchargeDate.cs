namespace VietRide.Trip.Application.Features.FareSurcharges;

internal static class FareSurchargeDate
{
    private static readonly TimeSpan IctOffset = TimeSpan.FromHours(7);

    public static DateOnly Today(DateTimeOffset utcNow)
        => DateOnly.FromDateTime(utcNow.ToOffset(IctOffset).DateTime);
}
