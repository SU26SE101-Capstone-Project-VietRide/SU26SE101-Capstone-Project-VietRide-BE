using System.Text.Json;
using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Trips.Operations;
using VietRide.Trip.Domain.Constants;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.DriverTrips.CompleteTrip;

public sealed class CompleteTripCommandHandler : IRequestHandler<CompleteTripCommand, CompleteTripResponse>
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private const string DriverRole = "DRIVER";
    private const string AssistantRole = "ASSISTANT";
    private const string EventType = "trip.trip.completed";

    private readonly ITripRepository tripRepository;
    private readonly ITripAuditLogRepository auditLogRepository;
    private readonly IIntegrationEventOutbox outbox;
    private readonly IUnitOfWork unitOfWork;
    private readonly IClock clock;

    public CompleteTripCommandHandler(
        ITripRepository tripRepository,
        ITripAuditLogRepository auditLogRepository,
        IIntegrationEventOutbox outbox,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        this.tripRepository = tripRepository;
        this.auditLogRepository = auditLogRepository;
        this.outbox = outbox;
        this.unitOfWork = unitOfWork;
        this.clock = clock;
    }

    public async Task<CompleteTripResponse> Handle(CompleteTripCommand request, CancellationToken cancellationToken)
    {
        var now = clock.UtcNow;
        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var trip = await tripRepository.AcquireForLifecycleTransitionAsync(request.TripId, cancellationToken)
                ?? throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip was not found.");

            EnsureAssigned(trip, request.ActorUserId, request.ActorRole);

            try
            {
                trip.CompleteManually(now, request.ActorUserId);
            }
            catch (InvalidOperationException exception)
            {
                throw new CodedConflictException("TRIP_INVALID_TRANSITION", exception.Message);
            }

            var metadata = JsonSerializer.Serialize(new
            {
                tripId = trip.Id,
                role = request.ActorRole,
            }, JsonOptions);
            await auditLogRepository.AddAsync(
                TripAuditLog.Create(
                    Guid.NewGuid(),
                    trip.Id,
                    request.ActorUserId,
                    TripAuditAction.TripCompletedManual,
                    metadata,
                    now),
                cancellationToken);

            var integrationEvent = new TripCompletedIntegrationEvent(
                trip.Id,
                trip.OperatorId,
                now,
                trip.HasSubstitution);
            await outbox.EnqueueAsync(
                EventType,
                JsonSerializer.Serialize(integrationEvent, JsonOptions),
                cancellationToken);

            await unitOfWork.SaveChangesAsync(cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);

            return new CompleteTripResponse(trip.Id, trip.Status.ToString(), now, request.ActorUserId);
        }
        catch
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static void EnsureAssigned(VietRide.Trip.Domain.Entities.Trip trip, Guid actorUserId, string actorRole)
    {
        var isAssigned = actorRole switch
        {
            DriverRole => trip.DriverUserId == actorUserId,
            AssistantRole => trip.AssistantUserId == actorUserId,
            _ => false,
        };
        if (!isAssigned)
        {
            throw new ForbiddenException("FORBIDDEN", "Trip is not assigned to this crew member.");
        }
    }
}
