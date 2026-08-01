namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public sealed record TripSummaryBatchOutcome(
    TripSummaryBatchOutcomeKind Kind,
    IReadOnlyList<TripSummarySnapshot> Summaries,
    string? ErrorMessage)
{
    public static TripSummaryBatchOutcome Success(IReadOnlyList<TripSummarySnapshot> summaries)
        => new(TripSummaryBatchOutcomeKind.Success, summaries, null);

    public static TripSummaryBatchOutcome TransportFailure(string errorMessage)
        => new(TripSummaryBatchOutcomeKind.TransportError, [], errorMessage);
}
