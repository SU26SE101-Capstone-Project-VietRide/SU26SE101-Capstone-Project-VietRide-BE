using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Parcel.Application.Features.Parcels.RecordRefund;

public sealed class RecordParcelRefundedCommandHandler
    : IRequestHandler<RecordParcelRefundedCommand, bool>
{
    private readonly IParcelRepository _parcels;
    private readonly IClock _clock;

    public RecordParcelRefundedCommandHandler(IParcelRepository parcels, IClock clock)
    {
        _parcels = parcels;
        _clock = clock;
    }

    public async Task<bool> Handle(
        RecordParcelRefundedCommand command,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(command.ReferenceType, "PARCEL_REFUND", StringComparison.Ordinal)
            || command.Amount <= 0)
        {
            return false;
        }

        var parcel = await _parcels.GetByIdAsync(command.ParcelId, cancellationToken);
        if (parcel is null || parcel.SenderUserId != command.UserId)
            return false;
        if (parcel.RefundedAmountVnd.Amount >= parcel.RefundDueVnd.Amount)
            return true;

        var newTotal = checked(parcel.RefundedAmountVnd.Amount + command.Amount);
        if (newTotal > parcel.RefundDueVnd.Amount)
            return false;

        return await _parcels.TryRecordRefundedAmountAsync(
            parcel.Id,
            parcel.RefundedAmountVnd,
            Money.FromRaw(newTotal),
            _clock.UtcNow,
            cancellationToken);
    }
}
