using System.Text.Json.Serialization;

namespace VietRide.Parcel.Application.Features.Parcels.OperatorStats;

public sealed record OperatorParcelStatsItemResponse(
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? Key,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? Count,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Guid? RouteId,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? RouteName,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] long? ParcelCount);
