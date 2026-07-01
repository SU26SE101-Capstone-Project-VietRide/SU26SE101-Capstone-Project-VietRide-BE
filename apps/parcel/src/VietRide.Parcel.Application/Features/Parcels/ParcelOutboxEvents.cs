using System.Text.Json;
using VietRide.Shared.Application.Outbox;

namespace VietRide.Parcel.Application.Features.Parcels;

public static class ParcelOutboxEvents
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public const string Created = "parcel.parcel.created";
    public const string Loaded = "parcel.parcel.loaded";
    public const string Unloaded = "parcel.parcel.unloaded";
    public const string DeliveredPendingConfirm = "parcel.parcel.delivered_pending_confirm";
    public const string DeliveryConfirmed = "parcel.parcel.delivery_confirmed";
    public const string DeliveryRejected = "parcel.parcel.delivery_rejected";
    public const string Cancelled = "parcel.parcel.cancelled";
    public const string Rejected = "parcel.parcel.rejected";
    public const string Returned = "parcel.parcel.returned";
    public const string AutoRejected = "parcel.parcel.auto_rejected";
    public const string ReviewRequested = "parcel.parcel.review_requested";
    public const string TransferInitiated = "parcel.parcel.transfer_initiated";
    public const string RefundInitiated = "parcel.refund.initiated";

    public static Task EnqueueAsync(
        IIntegrationEventOutbox outbox,
        string eventType,
        object payload,
        CancellationToken cancellationToken)
        => outbox.EnqueueAsync(eventType, JsonSerializer.Serialize(payload, JsonOptions), cancellationToken);
}
