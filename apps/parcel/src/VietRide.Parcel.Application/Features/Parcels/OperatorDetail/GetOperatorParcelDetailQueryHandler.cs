using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Application.Features.Parcels.OperatorList;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Parcel.Application.Features.Parcels.OperatorDetail;

public sealed class GetOperatorParcelDetailQueryHandler
    : IRequestHandler<GetOperatorParcelDetailQuery, OperatorParcelDetailResponse>
{
    private readonly IParcelRepository _parcelRepository;
    private readonly ITripServiceClient _tripService;
    private readonly IIdentityServiceClient _identityService;

    public GetOperatorParcelDetailQueryHandler(
        IParcelRepository parcelRepository,
        ITripServiceClient tripService,
        IIdentityServiceClient identityService)
    {
        _parcelRepository = parcelRepository;
        _tripService = tripService;
        _identityService = identityService;
    }

    public async Task<OperatorParcelDetailResponse> Handle(
        GetOperatorParcelDetailQuery query,
        CancellationToken cancellationToken)
    {
        var detail = await _parcelRepository.GetOperatorDetailAsync(
            query.ParcelId,
            query.OperatorId,
            cancellationToken);
        if (detail is null)
        {
            throw new CodedNotFoundException(
                "PARCEL_NOT_FOUND",
                $"Parcel with id '{query.ParcelId}' not found.");
        }

        var parcel = detail.Parcel;
        var tripOutcome = await _tripService.GetTripSummariesAsync(
            [parcel.TripId],
            cancellationToken);
        if (tripOutcome.Kind != TripSummaryBatchOutcomeKind.Success)
        {
            throw DependencyUnavailable("Trip data is temporarily unavailable.");
        }

        var tripSummary = SingleExpected(
            tripOutcome.Summaries,
            summary => summary.TripId,
            parcel.TripId,
            "Trip summary batch returned unusable data.");

        var identityOutcome = await _identityService.GetUsersAsync(
            [parcel.SenderUserId],
            cancellationToken);
        if (identityOutcome.Kind != IdentityUserBatchOutcomeKind.Success)
        {
            throw DependencyUnavailable("Identity data is temporarily unavailable.");
        }

        var sender = SingleExpected(
            identityOutcome.Users,
            user => user.Id,
            parcel.SenderUserId,
            "Identity batch returned unusable data.");
        var projection = GetOperatorParcelsQueryHandler.Map(parcel, tripSummary, sender);

        return new OperatorParcelDetailResponse(projection)
        {
            OperatorId = parcel.OperatorId,
            RecipientUserId = parcel.RecipientUserId,
            DropoffStopId = parcel.DropoffStopId,
            SenderEmail = sender?.Email,
            RecipientEmail = parcel.RecipientEmail,
            CheckInPhotoUrls = parcel.CheckInPhotoUrls,
            DeliveryPhotoUrls = parcel.DeliveryPhotoUrls,
            DeliveryMethod = parcel.DeliveryMethod.ToString(),
            DepositAmount = parcel.DepositAmount.Amount,
            OriginalDepositAmount = parcel.OriginalDepositAmount.Amount,
            DiscountAmount = parcel.DiscountAmount.Amount,
            VoucherCode = parcel.VoucherCode,
            VoucherUsageId = parcel.VoucherUsageId,
            AdditionalAmount = parcel.AdditionalAmount.Amount,
            EstimatedLengthCm = parcel.EstimatedLengthCm,
            EstimatedWidthCm = parcel.EstimatedWidthCm,
            EstimatedHeightCm = parcel.EstimatedHeightCm,
            EstimatedDimWeightKg = parcel.EstimatedDimWeightKg,
            ActualLengthCm = parcel.ActualLengthCm,
            ActualWidthCm = parcel.ActualWidthCm,
            ActualHeightCm = parcel.ActualHeightCm,
            ActualDimWeightKg = parcel.ActualDimWeightKg,
            EstimatedGrossPriceVnd = parcel.EstimatedGrossPriceVnd.Amount,
            FinalGrossPriceVnd = parcel.FinalGrossPriceVnd.Amount,
            DepositPercent = parcel.DepositPercent,
            DepositPaymentId = parcel.DepositPaymentId,
            BalancePaymentId = parcel.BalancePaymentId,
            CheckedInAt = parcel.CheckedInAt,
            CheckedInByUserId = parcel.CheckedInByUserId,
            ReweighedAt = parcel.ReweighedAt,
            ReweighedByUserId = parcel.ReweighedByUserId,
            PricePerKgVnd = parcel.PricePerKgVnd.Amount,
            MinimumPriceVnd = parcel.MinimumPriceVnd.Amount,
            DimWeightFactor = parcel.DimWeightFactor,
            SettlementPolicyVersion = parcel.SettlementPolicyVersion,
            LoadedAt = parcel.LoadedAt,
            LoadedByUserId = parcel.LoadedByUserId,
            UnloadedAt = parcel.UnloadedAt,
            DeliveredPendingConfirmAt = parcel.DeliveredPendingConfirmAt,
            ConfirmedAt = parcel.ConfirmedAt,
            ConfirmedByUserId = parcel.ConfirmedByUserId,
            RejectedAt = parcel.RejectedAt,
            PendingActionResumeStatus = parcel.PendingActionResumeStatus?.ToString(),
            RejectionReason = parcel.RejectionReason,
            CancellationReason = parcel.CancellationReason,
            ReviewDecision = parcel.ReviewDecision?.ToString(),
            ReviewedAt = parcel.ReviewedAt,
            ReviewedByUserId = parcel.ReviewedByUserId,
            TransferTargetTripId = parcel.TransferTargetTripId,
            TransferRequestedAt = parcel.TransferRequestedAt,
            TransferConfirmedAt = parcel.TransferConfirmedAt,
            TransferConfirmedByUserId = parcel.TransferConfirmedByUserId,
            ReturnReason = parcel.ReturnReason,
            ReturnedAt = parcel.ReturnedAt,
            ReturnedByUserId = parcel.ReturnedByUserId,
            StatusHistory = detail.StatusHistory
                .OrderBy(history => history.OccurredAt)
                .ThenBy(history => history.Id)
                .Select(history => new OperatorParcelStatusHistoryItemResponse(
                    history.Status.ToString(),
                    history.OccurredAt,
                    history.ActorType,
                    history.ActorId,
                    history.Source,
                    history.Reason))
                .ToArray(),
        };
    }

    private static T? SingleExpected<T>(
        IReadOnlyList<T> items,
        Func<T, Guid> keySelector,
        Guid expectedId,
        string errorMessage)
        where T : class
    {
        if (items.Count == 0)
            return null;
        if (items.Count != 1 || keySelector(items[0]) != expectedId)
            throw DependencyUnavailable(errorMessage);
        return items[0];
    }

    private static ParcelDependencyUnavailableException DependencyUnavailable(string message)
        => new("UPSTREAM_UNAVAILABLE", message);
}
