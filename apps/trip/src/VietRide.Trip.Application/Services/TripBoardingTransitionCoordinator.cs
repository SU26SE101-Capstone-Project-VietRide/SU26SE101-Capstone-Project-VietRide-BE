using System.Text.Json;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Abstractions.Services;
using VietRide.Trip.Domain.Constants;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Services;

public sealed class TripBoardingTransitionCoordinator : ITripBoardingTransitionCoordinator
{
    private const string DriverRole = "DRIVER";
    private const string OperatorAdminRole = "OPERATOR_ADMIN";
    private const string EventType = "trip.trip.boarding_started";
    private static readonly TimeSpan AutomaticEarlyWindow = TimeSpan.FromMinutes(30);
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly ITripRepository tripRepository;
    private readonly ITripAuditLogRepository auditLogRepository;
    private readonly IIntegrationEventOutbox outbox;
    private readonly IUnitOfWork unitOfWork;
    private readonly ITripBoardingWindowProvider windowProvider;

    public TripBoardingTransitionCoordinator(
        ITripRepository tripRepository,
        ITripAuditLogRepository auditLogRepository,
        IIntegrationEventOutbox outbox,
        IUnitOfWork unitOfWork,
        ITripBoardingWindowProvider windowProvider)
    {
        this.tripRepository = tripRepository;
        this.auditLogRepository = auditLogRepository;
        this.outbox = outbox;
        this.unitOfWork = unitOfWork;
        this.windowProvider = windowProvider;
    }

    public async Task<TripBoardingTransitionResult> StartManualAsync(
        Guid tripId,
        Guid actorUserId,
        string actorRole,
        Guid? operatorId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var trip = await tripRepository.AcquireForLifecycleTransitionAsync(tripId, cancellationToken)
                ?? throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip was not found.");

            EnsureManualActorCanBoard(trip, actorUserId, actorRole, operatorId);

            if (trip.Status == TripStatus.BOARDING)
            {
                await unitOfWork.RollbackAsync(cancellationToken);
                return ToResult(trip);
            }

            if (trip.Status != TripStatus.SCHEDULED)
            {
                throw new CodedConflictException(
                    "TRIP_INVALID_TRANSITION",
                    $"Trip cannot start boarding from status {trip.Status}.");
            }

            if (trip.DepartureDateTime > now.Add(windowProvider.ManualEarlyWindow))
            {
                throw new CodedConflictException(
                    "TRIP_BOARDING_TOO_EARLY",
                    "Trip is outside the manual boarding window.");
            }

            trip.MarkBoarding(now);
            await AddManualAuditAsync(trip, actorUserId, actorRole, now, cancellationToken);
            await EnqueueBoardingStartedAsync(trip.Id, now, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);

            return ToResult(trip);
        }
        catch
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }

    public async Task<bool> TryStartAutomaticAsync(
        Guid tripId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        await unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            var trip = await tripRepository.AcquireForLifecycleTransitionAsync(tripId, cancellationToken);
            if (trip is null
                || trip.Status != TripStatus.SCHEDULED
                || trip.DepartureDateTime > now.Add(AutomaticEarlyWindow))
            {
                await unitOfWork.RollbackAsync(cancellationToken);
                return false;
            }

            trip.MarkBoarding(now);
            await EnqueueBoardingStartedAsync(trip.Id, now, cancellationToken);
            await unitOfWork.SaveChangesAsync(cancellationToken);
            await unitOfWork.CommitAsync(cancellationToken);
            return true;
        }
        catch
        {
            await unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }
    }

    private static void EnsureManualActorCanBoard(
        Domain.Entities.Trip trip,
        Guid actorUserId,
        string actorRole,
        Guid? operatorId)
    {
        if (actorRole == DriverRole)
        {
            if (trip.DriverUserId != actorUserId)
            {
                throw new ForbiddenException("FORBIDDEN", "Trip is not assigned to this driver.");
            }

            return;
        }

        if (actorRole == OperatorAdminRole)
        {
            if (operatorId is null || trip.OperatorId != operatorId)
            {
                throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip was not found.");
            }

            return;
        }

        throw new ForbiddenException("FORBIDDEN", "Caller is not allowed to start trip boarding.");
    }

    private async Task AddManualAuditAsync(
        Domain.Entities.Trip trip,
        Guid actorUserId,
        string actorRole,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var metadata = JsonSerializer.Serialize(new
        {
            tripId = trip.Id,
            role = actorRole,
        }, JsonOptions);
        await auditLogRepository.AddAsync(
            TripAuditLog.Create(
                Guid.NewGuid(),
                trip.Id,
                actorUserId,
                TripAuditAction.TripBoardingStartedManual,
                metadata,
                now),
            cancellationToken);
    }

    private Task EnqueueBoardingStartedAsync(
        Guid tripId,
        DateTimeOffset now,
        CancellationToken cancellationToken) =>
        outbox.EnqueueAsync(
            EventType,
            JsonSerializer.Serialize(new { tripId, boardingStartedAt = now }, JsonOptions),
            cancellationToken);

    private static TripBoardingTransitionResult ToResult(Domain.Entities.Trip trip) =>
        new(trip.Id, trip.Status.ToString());
}
