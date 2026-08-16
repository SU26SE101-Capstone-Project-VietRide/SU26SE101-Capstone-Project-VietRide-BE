namespace VietRide.Trip.Application.Abstractions.Services;

public interface ITripBoardingWindowProvider
{
    TimeSpan ManualEarlyWindow { get; }
}
