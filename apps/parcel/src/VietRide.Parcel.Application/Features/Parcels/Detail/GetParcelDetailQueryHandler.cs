using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Abstractions.Services;
using VietRide.Parcel.Application.Features.Parcels.Create;
using VietRide.Parcel.Application.Features.Reliability.ReadModels;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Parcel.Application.Features.Parcels.Detail;

public sealed class GetParcelDetailQueryHandler
    : IRequestHandler<GetParcelDetailQuery, ParcelDetailResponse>
{
    private readonly IParcelRepository _parcelRepository;
    private readonly ITripServiceClient _tripClient;
    private readonly IParcelReliabilityReadModelService? _screenModels;

    public GetParcelDetailQueryHandler(
        IParcelRepository parcelRepository,
        ITripServiceClient tripClient,
        IParcelReliabilityReadModelService? screenModels = null)
    {
        _parcelRepository = parcelRepository;
        _tripClient = tripClient;
        _screenModels = screenModels;
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

        ParcelScreenReadModel? screen = null;
        if (_screenModels is not null)
        {
            var screens = await _screenModels.BuildAsync(
                [parcel],
                query.UserId,
                includeClaim: true,
                cancellationToken);
            screens.TryGetValue(parcel.Id, out screen);
            originStationName = screen?.Trip.Route?.Origin.Name;
            destinationStationName = screen?.Trip.Route?.Destination.Name;
            eta = screen?.Trip.Eta;
        }
        else
        {
            var tripOutcome = await _tripClient.GetTripParcelSnapshotAsync(parcel.TripId, cancellationToken);
            if (tripOutcome.Kind == TripSnapshotOutcomeKind.Success)
            {
                var trip = tripOutcome.Snapshot!;
                originStationName = trip.OriginStation.Name;
                destinationStationName = trip.DestinationStation.Name;
                eta = trip.EstimatedArrivalTime;
            }
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
            parcel.BookingId,
            parcel.DropoffStopId,
            parcel.Description,
            parcel.Quantity,
            parcel.PhotoUrl,
            parcel.CheckInPhotoUrls,
            parcel.DeliveryPhotoUrls,
            parcel.SizeCategory.ToString(),
            parcel.EstimatedWeightKg,
            parcel.ActualWeightKg,
            parcel.DeliveryMethod.ToString(),
            parcel.DepositAmount.Amount,
            parcel.OriginalDepositAmount.Amount,
            parcel.DiscountAmount.Amount,
            parcel.VoucherCode,
            parcel.VoucherUsageId,
            parcel.AdditionalAmount.Amount,
            parcel.EstimatedSizeCategory.ToString(),
            parcel.ActualSizeCategory?.ToString(),
            parcel.EstimatedLengthCm,
            parcel.EstimatedWidthCm,
            parcel.EstimatedHeightCm,
            parcel.EstimatedVolumeM3,
            parcel.EstimatedDimWeightKg,
            parcel.EstimatedChargeableWeightKg,
            parcel.ActualLengthCm,
            parcel.ActualWidthCm,
            parcel.ActualHeightCm,
            parcel.ActualVolumeM3,
            parcel.ActualDimWeightKg,
            parcel.ActualChargeableWeightKg,
            parcel.DeclaredValueVnd,
            parcel.EstimatedGrossPriceVnd.Amount,
            parcel.FinalGrossPriceVnd.Amount,
            parcel.DiscountAmountVnd.Amount,
            parcel.EstimatedTotalPriceVnd.Amount,
            parcel.FinalTotalPriceVnd.Amount,
            parcel.DepositPercent,
            parcel.DepositRequiredVnd.Amount,
            parcel.DepositPaidVnd.Amount,
            parcel.BalanceRequiredVnd.Amount,
            parcel.BalancePaidVnd.Amount,
            parcel.RefundDueVnd.Amount,
            parcel.RefundedAmountVnd.Amount,
            parcel.ForfeitedDepositVnd.Amount,
            parcel.DepositPaymentId,
            parcel.BalancePaymentId,
            parcel.LoadCutoffAt,
            parcel.LatestCheckInAt,
            parcel.CheckedInAt,
            parcel.CheckedInByUserId,
            parcel.ReweighedAt,
            parcel.ReweighedByUserId,
            parcel.FinalPaymentDeadline,
            parcel.PricePerKgVnd.Amount,
            parcel.MinimumPriceVnd.Amount,
            parcel.DimWeightFactor,
            parcel.SettlementPolicyVersion,
            parcel.CreatedAt,
            parcel.LoadedAt,
            parcel.UnloadedAt,
            parcel.DeliveredPendingConfirmAt,
            parcel.ConfirmedAt,
            parcel.RejectedAt,
            originStationName,
            destinationStationName,
            eta,
            screen?.Operator,
            screen?.Trip,
            screen?.DropoffLocation,
            new ParcelCompensationPolicySnapshotResponse(
                parcel.CompensationPolicyVersionSnapshot,
                parcel.CompensationRatePercentSnapshot,
                parcel.CompensationPolicyCapVndSnapshot,
                parcel.NoProofFallbackMultiplierSnapshot,
                parcel.ClaimWindowDaysSnapshot,
                parcel.SearchSlaHoursSnapshot,
                parcel.DecisionSlaBusinessDaysSnapshot,
                parcel.PayoutSlaBusinessDaysSnapshot),
            screen?.Reliability,
            screen?.Reliability.AvailableActions);
    }
}
