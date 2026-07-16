using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Parcel.Application.Features.Parcels.InternalDetail;

public sealed record GetParcelPaymentContextQuery(
    string ReferenceType,
    Guid ReferenceId) : IRequest<ParcelPaymentContextSnapshot>;

public sealed record ParcelPaymentContextSnapshot(
    int Version,
    bool CanBackfill,
    string? QuarantineReason,
    IReadOnlyList<ParcelPaymentAllocationSnapshot> Allocations);

public sealed record ParcelPaymentAllocationSnapshot(
    Guid ReferenceId,
    string ReferenceType,
    Guid OperatorId,
    Guid TripId,
    long GrossAmount,
    long VoucherVietRideFundedAmount,
    long VoucherOperatorFundedAmount);

public sealed class GetParcelPaymentContextQueryHandler
    : IRequestHandler<GetParcelPaymentContextQuery, ParcelPaymentContextSnapshot>
{
    private readonly IParcelRepository _parcels;

    public GetParcelPaymentContextQueryHandler(IParcelRepository parcels)
    {
        _parcels = parcels;
    }

    public async Task<ParcelPaymentContextSnapshot> Handle(
        GetParcelPaymentContextQuery request,
        CancellationToken cancellationToken)
    {
        if (request.ReferenceId == Guid.Empty
            || request.ReferenceType is not ("PARCEL" or "PARCEL_ADDITIONAL"))
        {
            throw new CodedValidationException(
                "PAYMENT_CONTEXT_REFERENCE_INVALID",
                "Parcel payment context supports PARCEL or PARCEL_ADDITIONAL references.");
        }

        var parcel = await _parcels.GetByIdAsync(request.ReferenceId, cancellationToken);
        if (parcel is null)
            throw new CodedNotFoundException("PARCEL_NOT_FOUND", "Parcel payment reference was not found.");

        if (parcel.VoucherUsageId.HasValue)
        {
            return new ParcelPaymentContextSnapshot(
                1,
                false,
                "LEGACY_VOUCHER_FUNDING_UNRESOLVED",
                []);
        }

        var amount = request.ReferenceType == "PARCEL"
            ? parcel.OriginalDepositAmount.Amount
            : parcel.AdditionalAmount.Amount;

        if (amount <= 0)
        {
            return new ParcelPaymentContextSnapshot(
                1,
                false,
                "LEGACY_PAYMENT_AMOUNT_UNRESOLVED",
                []);
        }

        return new ParcelPaymentContextSnapshot(
            1,
            true,
            null,
            [
                new ParcelPaymentAllocationSnapshot(
                    parcel.Id,
                    request.ReferenceType,
                    parcel.OperatorId,
                    parcel.TripId,
                    amount,
                    0,
                    0),
            ]);
    }
}
