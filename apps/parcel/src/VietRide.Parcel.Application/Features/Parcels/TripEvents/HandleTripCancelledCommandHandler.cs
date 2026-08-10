using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Application.Features.Parcels.TripCancellationImpact;
using VietRide.Shared.Application.Outbox;

namespace VietRide.Parcel.Application.Features.Parcels.TripEvents;

public sealed class HandleTripCancelledCommandHandler
    : IRequestHandler<HandleTripCancelledCommand, int>
{
    private const string RefundReason = "TRIP_CANCELLED_PRE_LOAD";

    private readonly IParcelRepository parcelRepository;
    private readonly ITripServiceClient tripClient;
    private readonly IIntegrationEventOutbox outbox;
    private readonly IParcelStatsRepository statsRepository;

    public HandleTripCancelledCommandHandler(
        IParcelRepository parcelRepository,
        ITripServiceClient tripClient,
        IIntegrationEventOutbox outbox,
        IParcelStatsRepository statsRepository)
    {
        this.parcelRepository = parcelRepository;
        this.tripClient = tripClient;
        this.outbox = outbox;
        this.statsRepository = statsRepository;
    }

    public async Task<int> Handle(
        HandleTripCancelledCommand command,
        CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var candidates = await parcelRepository.GetTripCancellationCandidatesAsync(
            command.TripId,
            command.OperatorId,
            cancellationToken);
        var changed = new List<(
            TripCancellationParcelCandidate Candidate,
            long RefundAmountVnd,
            ParcelTripCancellationDisposition Disposition)>();

        foreach (var candidate in candidates)
        {
            var classification = ParcelTripCancellationClassifier.Classify(candidate);
            if (classification.Disposition == ParcelTripCancellationDisposition.None)
            {
                continue;
            }

            var refundDueVnd = checked(
                candidate.RefundedAmountVnd + classification.RefundAmountVnd);
            var won = await parcelRepository.TryApplyTripCancellationAsync(
                candidate.ParcelId,
                command.OperatorId,
                candidate.Status,
                classification.TargetStatus!.Value,
                refundDueVnd,
                now,
                cancellationToken);
            if (!won)
            {
                continue;
            }

            if (classification.Disposition == ParcelTripCancellationDisposition.CancelAndRefund)
            {
                await EnsureCargoSuccessAsync(
                    await tripClient.ReleaseCargoAsync(
                        candidate.TripId,
                        candidate.ParcelId,
                        Positive(candidate.ActualWeightKg ?? candidate.EstimatedWeightKg),
                        Positive(candidate.ActualVolumeM3 ?? candidate.EstimatedVolumeM3),
                        ParcelOperationId.Create(
                            command.EventId,
                            candidate.ParcelId,
                            "TRIP_CANCELLED_CARGO_RELEASE"),
                        cancellationToken));

                await ParcelOutboxEvents.EnqueueTerminalAsync(
                    outbox,
                    ParcelOperationId.Create(
                        command.EventId,
                        candidate.ParcelId,
                        "TRIP_CANCELLED_TERMINAL"),
                    now,
                    ParcelOutboxEvents.Cancelled,
                    candidate.ParcelId,
                    candidate.ParcelCode,
                    candidate.OperatorId,
                    candidate.SenderUserId,
                    candidate.TripId,
                    classification.RefundAmountVnd,
                    RefundReason,
                    cancellationToken);

                if (classification.RefundAmountVnd > 0)
                {
                    var refundIdempotencyKey = ParcelOperationId.Create(
                        command.EventId,
                        candidate.ParcelId,
                        "TRIP_CANCELLED_REFUND");
                    await ParcelOutboxEvents.EnqueueCanonicalRefundAsync(
                        outbox,
                        ParcelOperationId.Create(
                            command.EventId,
                            candidate.ParcelId,
                            "TRIP_CANCELLED_REFUND_EVENT"),
                        now,
                        candidate.ParcelId,
                        candidate.SenderUserId,
                        classification.RefundAmountVnd,
                        RefundReason,
                        refundIdempotencyKey,
                        cancellationToken);
                }
            }
            else
            {
                var eventId = ParcelOperationId.Create(
                    command.EventId,
                    candidate.ParcelId,
                    "TRIP_CANCELLED_PENDING_OPERATOR_ACTION");
                await ParcelOutboxEvents.EnqueueAsync(
                    outbox,
                    eventId,
                    ParcelOutboxEvents.PendingOperatorAction,
                    new
                    {
                        eventId,
                        occurredAt = now,
                        parcelId = candidate.ParcelId,
                        parcelCode = candidate.ParcelCode,
                        operatorId = candidate.OperatorId,
                        userId = candidate.SenderUserId,
                        tripId = candidate.TripId,
                        reason = "TRIP_CANCELLED",
                    },
                    cancellationToken);
            }

            changed.Add((
                candidate,
                classification.RefundAmountVnd,
                classification.Disposition));
        }

        foreach (var group in changed
            .Where(item => item.Disposition
                == ParcelTripCancellationDisposition.CancelAndRefund)
            .GroupBy(item => item.Candidate.OperatorId))
        {
            await statsRepository.UpsertIncrementAsync(
                group.Key,
                VietRide.Shared.Kernel.Time.BusinessTime.ToLocalDate(now),
                0,
                0,
                0,
                group.Count(),
                0,
                0,
                group.Sum(item => item.RefundAmountVnd),
                cancellationToken);
        }

        return changed.Count;
    }

    private static decimal Positive(decimal value)
        => value > 0 ? value : 0.0001m;

    private static Task EnsureCargoSuccessAsync(TripCargoOutcome outcome)
    {
        if (outcome is null)
        {
            return Task.CompletedTask;
        }

        return outcome.Kind switch
        {
            TripCargoOutcomeKind.Success => Task.CompletedTask,
            TripCargoOutcomeKind.TripNotFound => throw new ParcelDependencyUnavailableException(
                "TRIP_SERVICE_UNAVAILABLE",
                outcome.ErrorMessage ?? "Trip was not found."),
            TripCargoOutcomeKind.CapacityExceeded => throw new ParcelDependencyUnavailableException(
                "TRIP_CARGO_TRANSFER_CONFLICT",
                outcome.ErrorMessage ?? "Trip cargo release lost a concurrent mutation."),
            _ => throw new ParcelDependencyUnavailableException(
                "TRIP_SERVICE_UNAVAILABLE",
                outcome.ErrorMessage ?? "Trip service unavailable."),
        };
    }
}
