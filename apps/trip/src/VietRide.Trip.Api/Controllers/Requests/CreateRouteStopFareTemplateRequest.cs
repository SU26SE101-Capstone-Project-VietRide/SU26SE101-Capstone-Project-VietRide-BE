namespace VietRide.Trip.Api.Controllers.Requests;

public sealed record CreateRouteStopFareTemplateRequest(
    Guid StopId,
    long FareFromThisStop,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveUntil);
