namespace VietRide.Trip.Infrastructure.Http;

public sealed class BookingImpactClientOptions
{
    public const string SectionName = "BookingImpact";
    public string ImpactPath { get; set; } = "/internal/v1/bookings/trips/{tripId}/edit-impact";
    public string VehicleSubstitutionImpactPath { get; set; }
        = "/internal/v1/bookings/trips/{tripId}/vehicle-substitution-impact";
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(5);
}
