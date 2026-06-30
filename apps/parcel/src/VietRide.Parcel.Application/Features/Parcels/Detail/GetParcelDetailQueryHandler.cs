using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Parcel.Application.Features.Parcels.Detail;

public sealed class GetParcelDetailQueryHandler
    : IRequestHandler<GetParcelDetailQuery, ParcelDetailResponse>
{
    private readonly IParcelRepository _parcelRepository;
    private readonly ITripServiceClient _tripClient;

    public GetParcelDetailQueryHandler(
        IParcelRepository parcelRepository,
        ITripServiceClient tripClient)
    {
        _parcelRepository = parcelRepository;
        _tripClient = tripClient;
    }

    public async Task<ParcelDetailResponse> Handle(
        GetParcelDetailQuery query,
        CancellationToken cancellationToken)
    {
        var parcel = await _parcelRepository.GetByIdAsync(query.ParcelId, cancellationToken);
        if (parcel is null)
            throw new CodedNotFoundException(
                "PARCEL_NOT_FOUND",
                $"Parcel with id '{query.ParcelId}' not found.");

        var isSender = query.UserId.HasValue && query.UserId.Value == parcel.SenderUserId;
        var isRecipient = query.UserId.HasValue && query.UserId.Value == parcel.RecipientUserId;
        var isOperator = query.OperatorId.HasValue && query.OperatorId.Value == parcel.OperatorId;

        if (!query.SkipAuthorization && !isSender && !isRecipient && !isOperator)
            throw new ForbiddenException(
                "FORBIDDEN",
                $"Caller is not authorized to view parcel '{query.ParcelId}'.");

        string? originStationName = null;
        string? destinationStationName = null;
        DateTimeOffset? eta = null;

        var tripOutcome = await _tripClient.GetTripParcelSnapshotAsync(parcel.TripId, cancellationToken);
        if (tripOutcome.Kind == TripSnapshotOutcomeKind.Success)
        {
            var trip = tripOutcome.Snapshot!;
            originStationName = trip.OriginStation.Name;
            destinationStationName = trip.DestinationStation.Name;
            eta = trip.EstimatedArrivalTime;
        }

        return new ParcelDetailResponse(
            parcel.Id,
            parcel.ParcelCode,
            parcel.Status.ToString(),
            parcel.SenderUserId,
            parcel.RecipientUserId,
            parcel.RecipientName,
            parcel.RecipientPhone.ToString(),
            parcel.OperatorId,
            parcel.TripId,
            parcel.DropoffStopId,
            parcel.Description,
            parcel.SizeCategory.ToString(),
            parcel.EstimatedWeightKg,
            parcel.ActualWeightKg,
            parcel.DeliveryMethod.ToString(),
            parcel.DepositAmount.Amount,
            parcel.AdditionalAmount.Amount,
            parcel.CreatedAt,
            parcel.LoadedAt,
            parcel.UnloadedAt,
            parcel.DeliveredPendingConfirmAt,
            parcel.ConfirmedAt,
            parcel.RejectedAt,
            originStationName,
            destinationStationName,
            eta);
    }
}
