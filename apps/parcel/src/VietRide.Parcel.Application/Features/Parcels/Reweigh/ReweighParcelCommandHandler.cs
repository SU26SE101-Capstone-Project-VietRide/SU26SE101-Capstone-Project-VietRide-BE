using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Parcel.Application.Features.Parcels.Reweigh;

public sealed class ReweighParcelCommandHandler
    : IRequestHandler<ReweighParcelCommand, ReweighParcelResponse>
{
    private static readonly TimeSpan AdditionalPaymentTimeout = TimeSpan.FromMinutes(15);

    private readonly IParcelRepository _parcelRepository;
    private readonly IParcelRouteFareRepository _fareRepository;
    private readonly ITripServiceClient _tripClient;
    private readonly IPaymentServiceClient _paymentClient;

    public ReweighParcelCommandHandler(
        IParcelRepository parcelRepository,
        IParcelRouteFareRepository fareRepository,
        ITripServiceClient tripClient,
        IPaymentServiceClient paymentClient)
    {
        _parcelRepository = parcelRepository;
        _fareRepository = fareRepository;
        _tripClient = tripClient;
        _paymentClient = paymentClient;
    }

    public async Task<ReweighParcelResponse> Handle(
        ReweighParcelCommand command,
        CancellationToken cancellationToken)
    {
        var parcel = await _parcelRepository.GetByIdAsync(command.ParcelId, cancellationToken);
        if (parcel is null)
            throw new CodedNotFoundException("PARCEL_NOT_FOUND", $"Parcel '{command.ParcelId}' not found.");

        if (parcel.OperatorId != command.OperatorId)
            throw new ForbiddenException("FORBIDDEN", "Parcel does not belong to this operator.");

        if (parcel.Status != ParcelStatus.PENDING)
            throw new CodedConflictException(
                "INVALID_STATUS",
                $"Parcel is in status '{parcel.Status}'. Only PENDING parcels can be reweighed.");

        if (!Enum.TryParse<ParcelSizeCategory>(command.ActualSizeCategory, ignoreCase: true, out var actualSize))
            throw new CodedValidationException(
                "INVALID_SIZE_CATEGORY",
                $"'{command.ActualSizeCategory}' is not a valid ParcelSizeCategory.");

        if (command.PaymentMethod is not ("WALLET" or "VNPAY"))
            throw new CodedValidationException(
                "VALIDATION_ERROR", "PaymentMethod must be WALLET or VNPAY.");

        if (command.ActualWeightKg <= 0)
            throw new CodedValidationException(
                "VALIDATION_ERROR", "Actual weight must be positive.");

        // Get routeId from trip to look up the fare for actual size category
        var tripOutcome = await _tripClient.GetTripParcelSnapshotAsync(parcel.TripId, cancellationToken);
        if (tripOutcome.Kind != TripSnapshotOutcomeKind.Success)
            throw new ParcelDependencyUnavailableException(
                "TRIP_SERVICE_UNAVAILABLE",
                tripOutcome.ErrorMessage ?? "Trip service unavailable.");

        var routeId = tripOutcome.Snapshot!.RouteId;

        // Look up fare for the actual size category
        var fare = await _fareRepository.FindByCompositeAsync(routeId, actualSize, cancellationToken);
        if (fare is null)
            throw new CodedValidationException(
                "FARE_NOT_CONFIGURED",
                $"No fare configured for route '{routeId}' and size category '{command.ActualSizeCategory}'.");

        var now = DateTimeOffset.UtcNow;
        var additionalRaw = Math.Max(0, fare.PriceVnd.Amount - parcel.DepositAmount.Amount);
        var additionalAmount = Money.FromRaw(additionalRaw);

        if (additionalRaw == 0)
        {
            // No additional fee - just record actual weight/size
            var snapshot = await _parcelRepository.TryReweighNoFeeAsync(
                command.ParcelId, command.ActualWeightKg, actualSize, now, cancellationToken);

            if (snapshot is null)
                throw new CodedConflictException(
                    "RACE_LOST", "Parcel status changed during reweigh.");

            return new ReweighParcelResponse(
                command.ParcelId, snapshot.ParcelCode, ParcelStatus.PENDING.ToString(), 0, null);
        }
        else
        {
            // Additional fee required - transition to PENDING_ADDITIONAL_PAYMENT
            var deadline = now + AdditionalPaymentTimeout;
            var snapshot = await _parcelRepository.TryReweighWithFeeAsync(
                command.ParcelId, command.ActualWeightKg, actualSize,
                additionalAmount, deadline, now, cancellationToken);

            if (snapshot is null)
                throw new CodedConflictException(
                    "RACE_LOST", "Parcel status changed during reweigh.");

            // Initiate additional payment
            var idempotencyKey = $"parcel:additional:{command.ParcelId}";
            var outcome = await _paymentClient.ChargeParcelPaymentAsync(
                "PARCEL_ADDITIONAL",
                command.ParcelId,
                parcel.SenderUserId,
                additionalAmount.Amount,
                command.PaymentMethod,
                idempotencyKey,
                cancellationToken);

            if (outcome.Kind == ChargeOutcomeKind.InsufficientFunds)
            {
                throw new CodedValidationException(
                    "INSUFFICIENT_FUNDS",
                    outcome.ErrorMessage ?? "Insufficient wallet balance.");
            }

            if (outcome.Kind == ChargeOutcomeKind.TransportError)
            {
                throw new ParcelDependencyUnavailableException(
                    "PAYMENT_SERVICE_ERROR",
                    outcome.ErrorMessage ?? "Payment service unavailable.");
            }

            // Store AdditionalPaymentId if returned (atomic ExecuteUpdateAsync, not stale entity)
            if (outcome.Result?.PaymentId is not null)
            {
                await _parcelRepository.TryAssignAdditionalPaymentIdAsync(
                    command.ParcelId, outcome.Result.PaymentId, now, cancellationToken);
            }

            return new ReweighParcelResponse(
                command.ParcelId, snapshot.ParcelCode,
                ParcelStatus.PENDING_ADDITIONAL_PAYMENT.ToString(),
                additionalAmount.Amount,
                outcome.Result?.PaymentRedirectUrl);
        }
    }
}
