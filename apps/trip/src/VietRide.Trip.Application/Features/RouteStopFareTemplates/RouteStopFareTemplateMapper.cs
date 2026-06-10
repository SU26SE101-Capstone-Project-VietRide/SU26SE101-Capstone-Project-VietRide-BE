using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.RouteStopFareTemplates;

internal static class RouteStopFareTemplateMapper
{
    public static RouteStopFareTemplateDto ToDto(RouteStopFareTemplate template)
        => new(
            template.Id,
            template.RouteId,
            template.StopId,
            template.FareFromThisStop.Amount,
            template.EffectiveFrom,
            template.EffectiveUntil,
            template.CreatedAt,
            template.UpdatedAt);
}
