using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Features.Parcels;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;

namespace VietRide.Parcel.Application.Features.Parcels.MarkLoaded;

public sealed class MarkParcelLoadedCommandHandler
    : IRequestHandler<MarkParcelLoadedCommand, MarkParcelLoadedResponse>
{
    private readonly IParcelRepository _parcelRepository;
    private readonly IIntegrationEventOutbox _outbox;
    private readonly IParcelStatsRepository _statsRepository;

    public MarkParcelLoadedCommandHandler(
        IParcelRepository parcelRepository,
        IIntegrationEventOutbox outbox,
        IParcelStatsRepository statsRepository)
    {
        _parcelRepository = parcelRepository;
        _outbox = outbox;
        _statsRepository = statsRepository;
    }

    public async Task<MarkParcelLoadedResponse> Handle(
        MarkParcelLoadedCommand command,
        CancellationToken cancellationToken)
    {
        var parcel = await _parcelRepository.GetByIdAsync(command.ParcelId, cancellationToken);
        if (parcel is null)
            throw new CodedNotFoundException(
                "PARCEL_NOT_FOUND",
                $"Parcel with id '{command.ParcelId}' not found.");

        if (parcel.Status != ParcelStatus.PENDING)
            throw new CodedConflictException(
                "INVALID_STATUS",
                $"Parcel '{command.ParcelId}' is in status '{parcel.Status}' and cannot be loaded.");

        if (parcel.TripId != command.TripId)
            throw new CodedNotFoundException(
                "PARCEL_NOT_FOUND",
                $"Parcel with id '{command.ParcelId}' not found.");

        if (parcel.ParcelCode != command.ParcelCode)
            throw new CodedNotFoundException(
                "PARCEL_NOT_FOUND",
                $"Parcel with id '{command.ParcelId}' not found.");

        var now = DateTimeOffset.UtcNow;
        var snapshot = await _parcelRepository.TryMarkLoadedAsync(
            command.ParcelId,
            command.TripId,
            command.ParcelCode,
            command.LoadedByUserId,
            now,
            cancellationToken);

        if (snapshot is null)
            throw new CodedConflictException(
                "RACE_LOST",
                $"Parcel '{command.ParcelId}' status changed concurrently; cannot mark loaded.");

        await ParcelOutboxEvents.EnqueueAsync(
            _outbox,
            ParcelOutboxEvents.Loaded,
            new { parcelId = snapshot.ParcelId, tripId = snapshot.TripId, actualWeightKg = parcel.ActualWeightKg ?? parcel.EstimatedWeightKg },
            cancellationToken);

        await _statsRepository.UpsertIncrementAsync(
            snapshot.OperatorId,
            DateOnly.FromDateTime(now.UtcDateTime),
            0, 1, 0, 0, 0, 0, 0,
            cancellationToken);

        return new MarkParcelLoadedResponse(snapshot.ParcelId, snapshot.ParcelCode, snapshot.Status.ToString());
    }
}
