namespace VietRide.Parcel.Application.Abstractions.Repositories;

public sealed record OperatorParcelStatsBucketReadModel(
    string? Key,
    Guid? RouteId,
    string? RouteName,
    long Count);
