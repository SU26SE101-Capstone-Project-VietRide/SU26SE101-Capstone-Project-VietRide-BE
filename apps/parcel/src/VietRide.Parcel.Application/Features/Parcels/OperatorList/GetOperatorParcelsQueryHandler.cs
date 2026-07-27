using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.Application.Features.Parcels.OperatorList;

public sealed class GetOperatorParcelsQueryHandler
    : IRequestHandler<GetOperatorParcelsQuery, PagedResult<OperatorParcelListItemResponse>>
{
    private readonly IParcelRepository _parcelRepository;

    public GetOperatorParcelsQueryHandler(IParcelRepository parcelRepository)
    {
        _parcelRepository = parcelRepository;
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

        var items = page.Items.Select(parcel => new OperatorParcelListItemResponse(
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
            parcel.CreatedAt)).ToList();

        return PagedResult<OperatorParcelListItemResponse>.Create(
            items,
            page.Page,
            page.PageSize,
            page.TotalItems);
    }

    private static TEnum? ParseOptional<TEnum>(string? value)
        where TEnum : struct, Enum
        => Enum.TryParse<TEnum>(value, ignoreCase: true, out var parsed) ? parsed : null;
}
