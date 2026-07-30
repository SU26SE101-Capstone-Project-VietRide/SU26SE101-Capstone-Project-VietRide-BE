namespace VietRide.Parcel.Application.Abstractions.ServiceClients;

public interface IParcelDeliveryEmailClient
{
    Task SendDeliveryLinkAsync(
        ParcelDeliveryEmailRequest request,
        CancellationToken cancellationToken = default);
}
