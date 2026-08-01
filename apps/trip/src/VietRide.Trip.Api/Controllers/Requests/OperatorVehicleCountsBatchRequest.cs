namespace VietRide.Trip.Api.Controllers.Requests;

public sealed record OperatorVehicleCountsBatchRequest(IReadOnlyList<Guid>? OperatorIds);
