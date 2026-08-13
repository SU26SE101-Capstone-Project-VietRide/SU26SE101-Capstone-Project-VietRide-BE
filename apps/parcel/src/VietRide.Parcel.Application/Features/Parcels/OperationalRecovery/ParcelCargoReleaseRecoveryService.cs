using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.Application.Features.Parcels.OperationalRecovery;

public sealed class ParcelCargoReleaseRecoveryService
{
    private readonly IParcelRepository _parcelRepository;
    private readonly ITripServiceClient _tripClient;

    public ParcelCargoReleaseRecoveryService(
        IParcelRepository parcelRepository,
        ITripServiceClient tripClient)
    {
        _parcelRepository = parcelRepository;
        _tripClient = tripClient;
    }

    public async Task<bool> ReleaseOrScheduleAsync(
        ParcelEntity parcel,
        string reason,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var operationId = GetOperationId(parcel.Id);
        var operation = await _parcelRepository.TryClaimCargoRecoveryReleaseAsync(
            operationId,
            parcel.Id,
            parcel.TripId,
            reason,
            now,
            cancellationToken);
        operation ??= await _parcelRepository.GetCargoRecoveryOperationAsync(operationId, cancellationToken);
        if (operation is null)
        {
            var active = await _parcelRepository.GetActiveCargoRecoveryOperationAsync(
                parcel.Id,
                cancellationToken);
            throw new CodedConflictException(
                "PARCEL_CARGO_RECOVERY_IN_PROGRESS",
                active is null
                    ? "Parcel cargo release could not be scheduled."
                    : $"Parcel has a pending {active.OperationType} cargo recovery operation.");
        }

        if (operation.OperationStatus == ParcelCargoRecoveryOperationStatus.COMPLETED)
            return true;

        if (operation.OperationType != ParcelCargoRecoveryOperationType.RELEASE)
        {
            throw new CodedConflictException(
                "PARCEL_CARGO_RECOVERY_IN_PROGRESS",
                $"Parcel has a pending {operation.OperationType} cargo recovery operation.");
        }

        try
        {
            var outcome = await _tripClient.ReleaseCargoAsync(
                operation.SourceTripId,
                operation.ParcelId,
                Positive(operation.WeightKg),
                Positive(operation.VolumeM3),
                operation.Id,
                cancellationToken);
            if (outcome.Kind != TripCargoOutcomeKind.Success)
                return false;

            return await _parcelRepository.TryCompleteCargoRecoveryReleaseAsync(
                operation.Id,
                now,
                cancellationToken)
                || (await _parcelRepository.GetCargoRecoveryOperationAsync(
                    operation.Id,
                    cancellationToken))?.OperationStatus == ParcelCargoRecoveryOperationStatus.COMPLETED;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch
        {
            return false;
        }
    }

    public async Task EnsurePendingReleaseCompletedAsync(
        Guid parcelId,
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var operation = await _parcelRepository.GetActiveCargoRecoveryOperationAsync(
            parcelId,
            cancellationToken);
        if (operation is null)
            return;

        if (operation.OperationType != ParcelCargoRecoveryOperationType.RELEASE)
        {
            throw new CodedConflictException(
                "PARCEL_CARGO_RECOVERY_IN_PROGRESS",
                $"Parcel has a pending {operation.OperationType} cargo recovery operation.");
        }

        var outcome = await _tripClient.ReleaseCargoAsync(
            operation.SourceTripId,
            operation.ParcelId,
            Positive(operation.WeightKg),
            Positive(operation.VolumeM3),
            operation.Id,
            cancellationToken);
        if (outcome.Kind != TripCargoOutcomeKind.Success)
        {
            throw new CodedConflictException(
                "PARCEL_CARGO_RECOVERY_IN_PROGRESS",
                "A previous cargo release is still pending recovery.");
        }

        await _parcelRepository.TryCompleteCargoRecoveryReleaseAsync(
            operation.Id,
            now,
            cancellationToken);
    }

    public static Guid GetOperationId(Guid parcelId) => parcelId;

    private static decimal Positive(decimal value) => value > 0m ? value : 0.0001m;
}
