using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.Application.Features.Parcels.DepositPayment;

public sealed class StartParcelDepositPaymentCommandHandler
    : IRequestHandler<StartParcelDepositPaymentCommand, ParcelDepositPaymentResponse>
{
    private readonly IParcelRepository _parcels;
    private readonly ITripServiceClient _trips;
    private readonly IPaymentServiceClient _payments;
    private readonly IBookingServiceClient _bookings;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IClock _clock;

    public StartParcelDepositPaymentCommandHandler(
        IParcelRepository parcels,
        ITripServiceClient trips,
        IPaymentServiceClient payments,
        IBookingServiceClient bookings,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _parcels = parcels;
        _trips = trips;
        _payments = payments;
        _bookings = bookings;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<ParcelDepositPaymentResponse> Handle(
        StartParcelDepositPaymentCommand command,
        CancellationToken cancellationToken)
    {
        var parcel = await _parcels.GetByIdAsync(command.ParcelId, cancellationToken);
        if (parcel is null)
            throw new CodedNotFoundException("PARCEL_NOT_FOUND", $"Parcel '{command.ParcelId}' not found.");
        if (parcel.SenderUserId != command.SenderUserId)
            throw new ForbiddenException("FORBIDDEN", "Only the sender can pay the parcel deposit.");
        if (parcel.Status != ParcelStatus.PENDING_PAYMENT)
            throw new CodedConflictException("INVALID_STATUS", $"Parcel is in status '{parcel.Status}'.");
        if (parcel.DepositPaymentId.HasValue)
            throw new CodedConflictException("PAYMENT_ALREADY_STARTED", "Deposit payment has already been started.");

        var now = _clock.UtcNow;
        if (!parcel.LatestCheckInAt.HasValue || now >= parcel.LatestCheckInAt.Value)
            throw new CodedConflictException("PARCEL_CHECK_IN_CLOSED", "Parcel check-in is already closed.");

        var voucherId = await ValidateVoucherAsync(parcel, command.PaymentMethod, cancellationToken);
        var operationId = Guid.TryParse(command.IdempotencyKey, out var parsedOperationId)
            ? parsedOperationId
            : parcel.Id;
        var reserve = await _trips.ReserveCargoAsync(
            parcel.TripId,
            parcel.Id,
            parcel.EstimatedWeightKg,
            parcel.EstimatedVolumeM3,
            operationId,
            cancellationToken);
        EnsureCargoReserved(reserve);

        if (parcel.DepositRequiredVnd.Amount == 0)
        {
            try
            {
                if (voucherId.HasValue)
                {
                    var usage = await _bookings.RecordVoucherUsageAsync(
                        voucherId.Value,
                        parcel.SenderUserId,
                        parcel.Id,
                        parcel.DiscountAmountVnd.Amount,
                        cancellationToken);
                    if (usage.Kind != VoucherUsageOutcomeKind.Success || !usage.UsageId.HasValue)
                        throw new ParcelDependencyUnavailableException(
                            "BOOKING_SERVICE_UNAVAILABLE",
                            usage.ErrorMessage ?? "Voucher usage could not be recorded.");
                    parcel.AttachVoucherUsage(usage.UsageId.Value);
                    await _unitOfWork.SaveChangesAsync(cancellationToken);
                }

                var activated = await _parcels.TryActivateZeroDepositAsync(parcel.Id, now, cancellationToken)
                    ?? throw new CodedConflictException("RACE_LOST", "Parcel status changed during deposit activation.");
                return new ParcelDepositPaymentResponse(
                    parcel.Id,
                    activated.Status.ToString(),
                    null,
                    0,
                    0,
                    null,
                    null);
            }
            catch
            {
                await _trips.ReleaseCargoAsync(
                    parcel.TripId,
                    parcel.Id,
                    parcel.EstimatedWeightKg,
                    parcel.EstimatedVolumeM3,
                    operationId,
                    cancellationToken);
                throw;
            }
        }

        var dueAt = Min(
            now.AddMinutes(ParcelCargoCalculator.DepositPaymentTimeoutMinutes),
            parcel.LatestCheckInAt.Value);
        var outcome = await _payments.ChargeParcelPaymentAsync(
            "PARCEL",
            parcel.Id,
            parcel.SenderUserId,
            parcel.DepositRequiredVnd.Amount,
            command.PaymentMethod,
            command.IdempotencyKey,
            cancellationToken,
            CreatePaymentContext(parcel),
            dueAt);

        if (outcome.Kind == ChargeOutcomeKind.InsufficientFunds)
        {
            await _trips.ReleaseCargoAsync(
                parcel.TripId,
                parcel.Id,
                parcel.EstimatedWeightKg,
                parcel.EstimatedVolumeM3,
                operationId,
                cancellationToken);
            throw new CodedValidationException(
                "INSUFFICIENT_FUNDS",
                outcome.ErrorMessage ?? "Insufficient wallet balance.");
        }

        if (outcome.Kind != ChargeOutcomeKind.Success || outcome.Result is null)
            throw new ParcelDependencyUnavailableException(
                "PAYMENT_SERVICE_ERROR",
                outcome.ErrorMessage ?? "Payment service unavailable.");

        var assigned = await _parcels.TryAssignDepositPaymentIdAsync(
            parcel.Id,
            outcome.Result.PaymentId,
            now,
            cancellationToken);
        if (!assigned)
            throw new CodedConflictException("RACE_LOST", "Parcel status changed while starting deposit payment.");

        return new ParcelDepositPaymentResponse(
            parcel.Id,
            ParcelStatus.PENDING_PAYMENT.ToString(),
            outcome.Result.PaymentId,
            parcel.DepositRequiredVnd.Amount,
            0,
            outcome.Result.DueAt ?? dueAt,
            outcome.Result.PaymentRedirectUrl);
    }

    private async Task<Guid?> ValidateVoucherAsync(
        Domain.Entities.Parcel parcel,
        string paymentMethod,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(parcel.VoucherCode))
            return null;

        var trip = await _trips.GetTripParcelSnapshotAsync(parcel.TripId, cancellationToken);
        if (trip.Kind != TripSnapshotOutcomeKind.Success || trip.Snapshot is null)
            throw new ParcelDependencyUnavailableException(
                "TRIP_SERVICE_UNAVAILABLE",
                trip.ErrorMessage ?? "Trip snapshot is unavailable for voucher validation.");

        var result = await _bookings.ValidateVoucherAsync(
            parcel.VoucherCode,
            parcel.OperatorId,
            trip.Snapshot.RouteId,
            parcel.SenderUserId,
            parcel.EstimatedGrossPriceVnd.Amount,
            paymentMethod,
            cancellationToken);
        if (result.Kind == VoucherValidationOutcomeKind.TransportError)
            throw new ParcelDependencyUnavailableException(
                "BOOKING_SERVICE_UNAVAILABLE",
                result.ErrorMessage ?? "Voucher validation is unavailable.");
        if (result.Kind != VoucherValidationOutcomeKind.Success
            || !result.VoucherId.HasValue
            || result.DiscountAmount != parcel.DiscountAmountVnd.Amount)
            throw new CodedValidationException(
                "VOUCHER_NOT_APPLICABLE",
                result.ErrorMessage ?? "Voucher is no longer applicable.");
        return result.VoucherId.Value;
    }

    private static PaymentContextSnapshot CreatePaymentContext(Domain.Entities.Parcel parcel)
        => new(1,
        [
            new PaymentAllocationSnapshot(
                parcel.Id,
                "PARCEL",
                parcel.OperatorId,
                parcel.TripId,
                parcel.DepositRequiredVnd.Amount,
                0,
                0),
        ]);

    private static void EnsureCargoReserved(TripCargoOutcome outcome)
    {
        if (outcome.Kind == TripCargoOutcomeKind.Success)
            return;
        if (outcome.Kind == TripCargoOutcomeKind.CapacityExceeded)
            throw new CodedConflictException(
                "TRIP_CARGO_CAPACITY_EXCEEDED",
                outcome.ErrorMessage ?? "Trip cargo capacity is insufficient.");
        throw new ParcelDependencyUnavailableException(
            outcome.Kind == TripCargoOutcomeKind.TripNotFound ? "TRIP_NOT_FOUND" : "TRIP_SERVICE_UNAVAILABLE",
            outcome.ErrorMessage ?? "Trip cargo reservation failed.");
    }

    private static DateTimeOffset Min(DateTimeOffset left, DateTimeOffset right)
        => left <= right ? left : right;
}
