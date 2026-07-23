using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Shared.Application.Outbox;

namespace VietRide.Parcel.Application.Features.Parcels.TripEvents;

public sealed class HandleTripCancelledCommandHandler
    : IRequestHandler<HandleTripCancelledCommand, int>
{
    private readonly IParcelRepository _parcelRepository;
    private readonly IIdentityServiceClient _identityClient;
    private readonly ITripServiceClient _tripClient;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IParcelStatsRepository _statsRepository;

    public HandleTripCancelledCommandHandler(
        IParcelRepository parcelRepository,
        IIdentityServiceClient identityClient,
        ITripServiceClient tripClient,
        IIntegrationEventOutbox outbox,
        IParcelStatsRepository statsRepository)
    {
        _parcelRepository = parcelRepository;
        _identityClient = identityClient;
        _tripClient = tripClient;
        _outbox = outbox;
        _statsRepository = statsRepository;
    }

    public async Task<int> Handle(
        HandleTripCancelledCommand command,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var rejected = await _parcelRepository.TryRejectPreAcceptanceByTripIdAsync(command.TripId, now, cancellationToken);
        var cancelled = await _parcelRepository.TryCancelPendingByTripIdAsync(command.TripId, now, cancellationToken);
        var operatorAction = await _parcelRepository.TryBulkSetPendingOperatorActionByTripIdAsync(command.TripId, now, cancellationToken);
        var refundAmounts = new Dictionary<Guid, long>();

        foreach (var parcel in rejected)
        {
            await ParcelOutboxEvents.EnqueueAsync(
                _outbox,
                ParcelOutboxEvents.Rejected,
                new
                {
                    parcelId = parcel.ParcelId,
                    parcelCode = parcel.ParcelCode,
                    operatorId = parcel.OperatorId,
                    userId = parcel.SenderUserId,
                    tripId = parcel.TripId,
                },
                cancellationToken);
        }

        foreach (var parcel in cancelled)
        {
            await EnsureCargoSuccessAsync(
                await _tripClient.ReleaseCargoAsync(
                    parcel.TripId,
                    parcel.ParcelId,
                    0m,
                    0.0001m,
                    parcel.ParcelId,
                    cancellationToken));

            var refundAmount = await ParcelRefundAmountCalculator.CalculateRefundAsync(
                _identityClient,
                parcel.OperatorId,
                parcel.DepositAmount + parcel.AdditionalAmount,
                cancellationToken);
            refundAmounts[parcel.ParcelId] = refundAmount;
            await ParcelOutboxEvents.EnqueueAsync(
                _outbox,
                ParcelOutboxEvents.Cancelled,
                new
                {
                    parcelId = parcel.ParcelId,
                    parcelCode = parcel.ParcelCode,
                    operatorId = parcel.OperatorId,
                    userId = parcel.SenderUserId,
                    tripId = parcel.TripId,
                    refundAmount,
                },
                cancellationToken);
            await ParcelOutboxEvents.EnqueueRefundAsync(
                _outbox,
                parcel.ParcelId,
                parcel.SenderUserId,
                refundAmount,
                cancellationToken);
        }

        foreach (var parcel in operatorAction)
        {
            await ParcelOutboxEvents.EnqueueAsync(
                _outbox,
                ParcelOutboxEvents.PendingOperatorAction,
                new
                {
                    parcelId = parcel.ParcelId,
                    parcelCode = parcel.ParcelCode,
                    operatorId = parcel.OperatorId,
                    userId = parcel.SenderUserId,
                    tripId = parcel.TripId,
                },
                cancellationToken);
        }

        foreach (var group in rejected.GroupBy(parcel => parcel.OperatorId))
        {
            await _statsRepository.UpsertIncrementAsync(
                group.Key,
                DateOnly.FromDateTime(now.UtcDateTime),
                0, 0, 0, group.Count(), 0, 0, 0,
                cancellationToken);
        }

        foreach (var group in cancelled.GroupBy(parcel => parcel.OperatorId))
        {
            var totalRefunded = group.Sum(parcel => refundAmounts[parcel.ParcelId]);

            await _statsRepository.UpsertIncrementAsync(
                group.Key,
                DateOnly.FromDateTime(now.UtcDateTime),
                0, 0, 0, group.Count(), 0, 0, totalRefunded,
                cancellationToken);
        }

        return rejected.Count + cancelled.Count + operatorAction.Count;
    }

    private static Task EnsureCargoSuccessAsync(TripCargoOutcome outcome)
    {
        if (outcome is null)
            return Task.CompletedTask;

        return outcome.Kind switch
        {
            TripCargoOutcomeKind.Success => Task.CompletedTask,
            TripCargoOutcomeKind.TripNotFound => throw new ParcelDependencyUnavailableException(
                "TRIP_NOT_FOUND",
                outcome.ErrorMessage ?? "Trip was not found."),
            TripCargoOutcomeKind.CapacityExceeded => throw new ParcelDependencyUnavailableException(
                "TRIP_CARGO_CAPACITY_EXCEEDED",
                outcome.ErrorMessage ?? "Trip cargo capacity would be exceeded."),
            _ => throw new ParcelDependencyUnavailableException(
                "TRIP_SERVICE_UNAVAILABLE",
                outcome.ErrorMessage ?? "Trip service unavailable."),
        };
    }
}
