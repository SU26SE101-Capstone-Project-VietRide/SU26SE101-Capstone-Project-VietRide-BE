namespace VietRide.Parcel.Api.Controllers.Requests;

public sealed record ManualConfirmDeliveryRequest(
    string? ConfirmNote = null,
    string? Note = null)
{
    public string ResolveNote()
        => ConfirmNote ?? Note ?? string.Empty;
}
