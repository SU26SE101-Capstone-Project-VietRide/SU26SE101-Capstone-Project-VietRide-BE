namespace VietRide.Trip.Application.Features.RouteStopFareTemplates;

public sealed record RouteStopFareTemplateDto(
    Guid Id,
    Guid RouteId,
    Guid StopId,
    long FareFromThisStop,
    DateTimeOffset EffectiveFrom,
    DateTimeOffset? EffectiveUntil,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);
