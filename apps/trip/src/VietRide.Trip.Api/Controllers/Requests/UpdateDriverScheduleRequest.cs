using System.Text.Json.Serialization;

namespace VietRide.Trip.Api.Controllers.Requests;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class UpdateDriverScheduleRequest
{
    private TimeOnly? departureTime;
    private IReadOnlyList<int>? dayOfWeek;
    private Guid? driverUserId;
    private Guid? assistantUserId;
    private Guid? vehicleId;
    private DateOnly? validUntil;
    private bool? isActive;

    public TimeOnly? DepartureTime { get => departureTime; set { departureTime = value; DepartureTimeSpecified = true; } }

    public IReadOnlyList<int>? DayOfWeek { get => dayOfWeek; set { dayOfWeek = value; DayOfWeekSpecified = true; } }

    public Guid? DriverUserId { get => driverUserId; set { driverUserId = value; DriverUserIdSpecified = true; } }

    public Guid? AssistantUserId { get => assistantUserId; set { assistantUserId = value; AssistantUserIdSpecified = true; } }

    public Guid? VehicleId { get => vehicleId; set { vehicleId = value; VehicleIdSpecified = true; } }

    public DateOnly? ValidUntil { get => validUntil; set { validUntil = value; ValidUntilSpecified = true; } }

    public bool? IsActive { get => isActive; set { isActive = value; IsActiveSpecified = true; } }

    [JsonIgnore] public bool DepartureTimeSpecified { get; private set; }

    [JsonIgnore] public bool DayOfWeekSpecified { get; private set; }

    [JsonIgnore] public bool DriverUserIdSpecified { get; private set; }

    [JsonIgnore] public bool AssistantUserIdSpecified { get; private set; }

    [JsonIgnore] public bool VehicleIdSpecified { get; private set; }

    [JsonIgnore] public bool ValidUntilSpecified { get; private set; }

    [JsonIgnore] public bool IsActiveSpecified { get; private set; }
}
