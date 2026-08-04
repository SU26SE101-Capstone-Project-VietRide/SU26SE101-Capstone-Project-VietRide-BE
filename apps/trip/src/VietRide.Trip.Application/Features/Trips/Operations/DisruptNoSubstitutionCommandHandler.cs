using System.Text.Json;
using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.Trips.Operations;

public sealed class DisruptNoSubstitutionCommandHandler : IRequestHandler<DisruptNoSubstitutionCommand, DisruptNoSubstitutionResponse>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly ITripRepository tripRepository;
    private readonly IIntegrationEventOutbox outbox;
    private readonly IUnitOfWork unitOfWork;
    private readonly IClock clock;
    private readonly IRouteChangeProposalLifecycleService? routeChangeProposals;

    public DisruptNoSubstitutionCommandHandler(
        ITripRepository tripRepository,
        IIntegrationEventOutbox outbox,
        IUnitOfWork unitOfWork,
        IClock clock,
        IRouteChangeProposalLifecycleService? routeChangeProposals = null)
    {
        this.tripRepository = tripRepository;
        this.outbox = outbox;
        this.unitOfWork = unitOfWork;
        this.clock = clock;
        this.routeChangeProposals = routeChangeProposals;
    }

    public async Task<DisruptNoSubstitutionResponse> Handle(DisruptNoSubstitutionCommand request, CancellationToken cancellationToken)
    {
        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var trip = await tripRepository.GetForUpdateAsync(request.TripId, cancellationToken)
                ?? throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip was not found.");
            if (trip.OperatorId != request.OperatorId)
            {
                throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip was not found.");
            }

            if (trip.Status is TripStatus.COMPLETED or TripStatus.CANCELLED or TripStatus.DISRUPTED)
            {
                throw new CodedConflictException("TRIP_ALREADY_TERMINAL", "Trip is already terminal.");
            }

            if (trip.Status != TripStatus.IN_PROGRESS)
            {
                throw new CodedValidationException(
                    "TRIP_NOT_IN_PROGRESS",
                    "Only an in-progress Trip can be disrupted without a substitution.");
            }

            var now = clock.UtcNow;
            var reason = request.Reason.Trim();
            trip.Disrupt(now, reason);
            if (routeChangeProposals is not null)
                await routeChangeProposals.ExpirePendingForTripAsync(trip.Id, now, cancellationToken);
            trip.MarkSubstitution(false);
            var eventId = Guid.NewGuid();
            var evt = new TripDisruptedIntegrationEvent(
                eventId,
                trip.Id,
                trip.OperatorId,
                now,
                hasSubstitution: false,
                reason);
            await outbox.EnqueueAsync(
                eventId,
                evt.EventType,
                JsonSerializer.Serialize(evt, JsonOptions),
                cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            return new DisruptNoSubstitutionResponse(
                trip.Id,
                trip.Status.ToString(),
                now,
                trip.HasSubstitution,
                trip.DisruptionReason!);
        }
        catch
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }
}
