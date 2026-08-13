namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public sealed record RouteSearchOutcome(
    bool Succeeded,
    IReadOnlyList<Guid> RouteIds,
    string? Message)
{
    public static RouteSearchOutcome Success(IReadOnlyList<Guid> routeIds) =>
        new(true, routeIds, null);

    public static RouteSearchOutcome Failure(string message) =>
        new(false, [], message);
}
