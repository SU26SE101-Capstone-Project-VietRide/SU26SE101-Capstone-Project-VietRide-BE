using VietRide.Shared.Application.Cqrs;
using VietRide.Trip.Application.Abstractions.Services;

namespace VietRide.Trip.Application.Features.Shuttle;

public sealed record GetDriverShuttleManifestQuery(
    Guid ShuttleTripId,
    Guid DriverUserId) : IQuery<ShuttleDriverManifest>;
