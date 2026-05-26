namespace VietRide.Shared.Application.Services;

/// Marker interface for application services. Used by DI registration scanning convention.
/// Per-aggregate services (e.g. IBookingService) inherit this so AddApplication() auto-registers.
public interface IApplicationService
{
}
