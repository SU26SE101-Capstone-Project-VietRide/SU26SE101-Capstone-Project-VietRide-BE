using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Parcel.Application.Features.Parcels.MarkLoaded;

public sealed class MarkParcelLoadedCommandHandler
    : IRequestHandler<MarkParcelLoadedCommand, MarkParcelLoadedResponse>
{
    private readonly IParcelRepository _parcelRepository;

    public MarkParcelLoadedCommandHandler(IParcelRepository parcelRepository)
    {
        _parcelRepository = parcelRepository;
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
            now,
            cancellationToken);

        if (snapshot is null)
            throw new CodedConflictException(
                "RACE_LOST",
                $"Parcel '{command.ParcelId}' status changed concurrently; cannot mark loaded.");

        return new MarkParcelLoadedResponse(snapshot.ParcelId, snapshot.ParcelCode, snapshot.Status.ToString());
    }
}
