using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Kernel.Primitives;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.Application.Features.Parcels.OperatorList;

public sealed class GetOperatorParcelsQueryHandler
    : IRequestHandler<GetOperatorParcelsQuery, PagedResult<OperatorParcelListItemResponse>>
{
    private readonly IParcelRepository _parcelRepository;
    private readonly ITripServiceClient _tripService;
    private readonly IIdentityServiceClient _identityService;

    public GetOperatorParcelsQueryHandler(
        IParcelRepository parcelRepository,
        ITripServiceClient tripService,
        IIdentityServiceClient identityService)
    {
        _parcelRepository = parcelRepository;
        _tripService = tripService;
        _identityService = identityService;
    }

    public async Task<PagedResult<OperatorParcelListItemResponse>> Handle(
        GetOperatorParcelsQuery query,
        CancellationToken cancellationToken)
    {
        var status = ParseOptional<ParcelStatus>(query.Status);
        var pendingActionType = ParseOptional<PendingActionType>(query.PendingActionType);
        var page = await _parcelRepository.ListByOperatorAsync(
            query.OperatorId,
            status,
            query.TripId,
            pendingActionType,
            query.Page,
            query.PageSize,
            cancellationToken);

        if (page.Items.Count == 0)
        {
            return PagedResult<OperatorParcelListItemResponse>.Create(
                [],
                page.Page,
                page.PageSize,
                page.TotalItems);
        }

        var tripOutcome = await _tripService.GetTripSummariesAsync(
            page.Items.Select(parcel => parcel.TripId).Distinct().ToArray(),
            cancellationToken);
        if (tripOutcome.Kind != TripSummaryBatchOutcomeKind.Success)
        {
            throw new ParcelDependencyUnavailableException(
                "UPSTREAM_UNAVAILABLE",
                "Trip data is temporarily unavailable.");
        }

        var identityOutcome = await _identityService.GetUsersAsync(
            page.Items.Select(parcel => parcel.SenderUserId).Distinct().ToArray(),
            cancellationToken);
        if (identityOutcome.Kind != IdentityUserBatchOutcomeKind.Success)
        {
            throw new ParcelDependencyUnavailableException(
                "UPSTREAM_UNAVAILABLE",
                "Identity data is temporarily unavailable.");
        }

        var tripSummaries = ToUniqueDictionary(
            tripOutcome.Summaries,
            summary => summary.TripId,
            "Trip summary batch returned duplicate ids.");
        var users = ToUniqueDictionary(
            identityOutcome.Users,
            user => user.Id,
            "Identity user batch returned duplicate ids.");

        var items = page.Items.Select(parcel => Map(
            parcel,
            tripSummaries.GetValueOrDefault(parcel.TripId),
            users.GetValueOrDefault(parcel.SenderUserId))).ToList();

        return PagedResult<OperatorParcelListItemResponse>.Create(
            items,
            page.Page,
            page.PageSize,
            page.TotalItems);
    }

    internal static OperatorParcelListItemResponse Map(
        ParcelEntity parcel,
        TripSummarySnapshot? tripSummary,
        IdentityUserSummary? sender)
    {
        var hasCompleteDisplaySnapshot = HasCompleteDisplaySnapshot(parcel);
        var route = hasCompleteDisplaySnapshot
            ? new OperatorParcelRouteResponse(
                parcel.TripSnapshotRouteId!.Value,
                parcel.TripSnapshotRouteName!,
                parcel.TripSnapshotOriginStationName!,
                parcel.TripSnapshotDestinationStationName!)
            : tripSummary is null
                ? null
                : new OperatorParcelRouteResponse(
                    tripSummary.Route.RouteId,
                    tripSummary.Route.Name,
                    tripSummary.Route.OriginName,
                    tripSummary.Route.DestinationName);
        var vehicle = hasCompleteDisplaySnapshot
            ? new OperatorParcelVehicleResponse(
                parcel.TripSnapshotVehicleId!.Value,
                parcel.TripSnapshotVehicleLicensePlate!)
            : tripSummary is null
                ? null
                : new OperatorParcelVehicleResponse(
                    tripSummary.Vehicle.VehicleId,
                    tripSummary.Vehicle.LicensePlate);

        return new OperatorParcelListItemResponse(
            parcel.Id,
            parcel.ParcelCode,
            parcel.Status.ToString(),
            parcel.TripId,
            parcel.SenderUserId,
            parcel.RecipientName,
            parcel.RecipientPhone.ToString(),
            parcel.EstimatedSizeCategory.ToString(),
            parcel.ActualSizeCategory?.ToString(),
            parcel.EstimatedChargeableWeightKg,
            parcel.ActualChargeableWeightKg,
            parcel.DepositRequiredVnd.Amount,
            parcel.DepositPaidVnd.Amount,
            parcel.BalanceRequiredVnd.Amount,
            parcel.BalancePaidVnd.Amount,
            parcel.RefundDueVnd.Amount,
            parcel.ForfeitedDepositVnd.Amount,
            parcel.LatestCheckInAt,
            parcel.LoadCutoffAt,
            parcel.FinalPaymentDeadline,
            parcel.PendingActionType?.ToString(),
            parcel.PendingActionReason,
            parcel.PhotoUrl,
            parcel.CreatedAt,
            new OperatorParcelTripResponse(
                parcel.TripId,
                tripSummary?.Status,
                tripSummary?.DepartureAt,
                tripSummary?.ArrivalEstimate,
                vehicle),
            route,
            new OperatorParcelUserResponse(
                parcel.SenderUserId,
                sender?.DisplayName,
                sender?.Phone),
            new OperatorParcelUserResponse(
                parcel.RecipientUserId,
                parcel.RecipientName,
                parcel.RecipientPhone.ToString()),
            (parcel.ActualSizeCategory ?? parcel.EstimatedSizeCategory).ToString(),
            parcel.Description,
            parcel.EstimatedWeightKg,
            parcel.ActualWeightKg,
            parcel.EstimatedVolumeM3,
            parcel.ActualVolumeM3,
            parcel.EstimatedTotalPriceVnd.Amount,
            parcel.FinalTotalPriceVnd.Amount,
            parcel.DiscountAmountVnd.Amount,
            parcel.RefundedAmountVnd.Amount,
            parcel.UpdatedAt);
    }

    private static bool HasCompleteDisplaySnapshot(ParcelEntity parcel)
        => parcel.TripSnapshotRouteId.HasValue
            && parcel.TripSnapshotRouteName is not null
            && parcel.TripSnapshotOriginStationName is not null
            && parcel.TripSnapshotDestinationStationName is not null
            && parcel.TripSnapshotVehicleId.HasValue
            && parcel.TripSnapshotVehicleLicensePlate is not null;

    private static IReadOnlyDictionary<Guid, T> ToUniqueDictionary<T>(
        IReadOnlyList<T> items,
        Func<T, Guid> keySelector,
        string duplicateMessage)
    {
        var dictionary = new Dictionary<Guid, T>();
        foreach (var item in items)
        {
            if (!dictionary.TryAdd(keySelector(item), item))
            {
                throw new ParcelDependencyUnavailableException(
                    "UPSTREAM_UNAVAILABLE",
                    duplicateMessage);
            }
        }

        return dictionary;
    }

    private static TEnum? ParseOptional<TEnum>(string? value)
        where TEnum : struct, Enum
        => Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) ? parsed : null;
}
