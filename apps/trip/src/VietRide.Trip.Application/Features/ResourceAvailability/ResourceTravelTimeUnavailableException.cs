using VietRide.Shared.Application.Exceptions;

namespace VietRide.Trip.Application.Features.ResourceAvailability;

public sealed class ResourceTravelTimeUnavailableException : Exception, ICodedHttpException
{
    public int StatusCode => 503;
    public string ErrorCode => "RESOURCE_TRAVEL_TIME_UNAVAILABLE";
    public IReadOnlyList<ValidationError>? Errors { get; }

    public ResourceTravelTimeUnavailableException(string message)
        : base(message)
    {
        Errors = [new ValidationError("location", message)];
    }
}
