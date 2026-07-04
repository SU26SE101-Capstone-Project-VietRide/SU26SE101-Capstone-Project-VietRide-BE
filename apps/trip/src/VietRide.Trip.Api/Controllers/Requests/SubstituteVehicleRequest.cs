namespace VietRide.Trip.Api.Controllers.Requests;

public sealed record SubstituteVehicleRequest(
    Guid NewVehicleId,
    Guid NewDriverUserId,
    Guid? NewAssistantUserId,
    string Reason);
