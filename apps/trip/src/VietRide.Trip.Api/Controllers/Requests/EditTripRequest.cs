using System.Text.Json.Serialization;

namespace VietRide.Trip.Api.Controllers.Requests;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class EditTripRequest
{
    private long? baseFare;
    private string? notes;
    private Guid? vehicleId;
    private Guid? routeId;

    public long? BaseFare
    {
        get => baseFare;
        set
        {
            baseFare = value;
            BaseFareSpecified = true;
        }
    }

    public string? Notes
    {
        get => notes;
        set
        {
            notes = value;
            NotesSpecified = true;
        }
    }

    public Guid? VehicleId
    {
        get => vehicleId;
        set
        {
            vehicleId = value;
            VehicleIdSpecified = true;
        }
    }

    public Guid? RouteId
    {
        get => routeId;
        set
        {
            routeId = value;
            RouteIdSpecified = true;
        }
    }

    [JsonIgnore]
    public bool BaseFareSpecified { get; private set; }

    [JsonIgnore]
    public bool NotesSpecified { get; private set; }

    [JsonIgnore]
    public bool VehicleIdSpecified { get; private set; }

    [JsonIgnore]
    public bool RouteIdSpecified { get; private set; }
}
