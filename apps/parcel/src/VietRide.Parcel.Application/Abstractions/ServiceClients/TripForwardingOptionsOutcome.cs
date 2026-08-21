namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public sealed record TripForwardingOptionsOutcome(
    bool IsSuccess,
    IReadOnlyList<TripForwardingOptionSnapshot> Options,
    string? ErrorMessage)
{
    public static TripForwardingOptionsOutcome Success(IReadOnlyList<TripForwardingOptionSnapshot> options)
        => new(true, options, null);

    public static TripForwardingOptionsOutcome Failure(string errorMessage)
        => new(false, [], errorMessage);
}
