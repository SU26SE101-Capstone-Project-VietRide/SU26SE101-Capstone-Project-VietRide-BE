namespace VietRide.Trip.Api.Controllers.Requests;

public sealed record UpdateDriverScheduleCrewRequest(
    Guid DriverUserId,
    Guid? AssistantUserId);
