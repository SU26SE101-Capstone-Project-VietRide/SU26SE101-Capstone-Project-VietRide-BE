using VietRide.Shared.Kernel.Time;

namespace VietRide.Trip.Application.Features.FareSurcharges;

internal static class FareSurchargeDate
{
    public static DateOnly Today(DateTimeOffset utcNow)
        => BusinessTime.ToLocalDate(utcNow);
}
