using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.Application.Features.Parcels.OperationalRecovery;

public sealed class ReturnParcelCommandHandler
    : IRequestHandler<ReturnParcelCommand, OperationalParcelResponse>
{
    private readonly IParcelRepository _parcelRepository;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMediator _mediator;
    private readonly IClock _clock;

    public ReturnParcelCommandHandler(
        IParcelRepository parcelRepository,
        IUnitOfWork unitOfWork,
        IMediator mediator,
        IClock clock)
    {
        _parcelRepository = parcelRepository;
        _unitOfWork = unitOfWork;
        _mediator = mediator;
        _clock = clock;
    }

    public async Task<OperationalParcelResponse> Handle(
        ReturnParcelCommand command,
        CancellationToken cancellationToken)
    {
        var parcel = await _parcelRepository.GetByIdAsync(
            command.ParcelId,
            cancellationToken);
        if (parcel is null)
        {
            throw new CodedNotFoundException(
                "PARCEL_NOT_FOUND",
                $"Parcel '{command.ParcelId}' not found.");
        }

        if (parcel.OperatorId != command.OperatorId)
        {
            throw new ForbiddenException(
                "FORBIDDEN",
                "Parcel does not belong to this operator.");
        }

        if (parcel.Status is not (
            ParcelStatus.PENDING_OPERATOR_ACTION
            or ParcelStatus.TRANSFER_ESCALATED))
        {
            throw new CodedConflictException(
                "INVALID_STATUS",
                $"Parcel status '{parcel.Status}' cannot be returned.");
        }

        var reason = command.Reason?.Trim();
        if (string.IsNullOrEmpty(reason) || reason.Length > 500)
        {
            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "Return reason must contain between 1 and 500 characters.");
        }

        var active = await _parcelRepository.GetActiveCargoRecoveryOperationAsync(
            parcel.Id,
            cancellationToken);
        if (active is not null)
        {
            EnsureMatchingReturn(active, parcel.TripId);
            return await _mediator.Send(
                new ResumeCargoRecoveryOperationCommand(active.Id),
                cancellationToken);
        }

        var operation = await ClaimReturnAsync(
            command,
            parcel.TripId,
            reason,
            cancellationToken);
        return await _mediator.Send(
            new ResumeCargoRecoveryOperationCommand(operation.Id),
            cancellationToken);
    }

    private async Task<ParcelCargoRecoveryOperationSnapshot> ClaimReturnAsync(
        ReturnParcelCommand command,
        Guid sourceTripId,
        string reason,
        CancellationToken cancellationToken)
    {
        ParcelCargoRecoveryOperationSnapshot? claimed;
        await _unitOfWork.BeginTransactionAsync(cancellationToken);
        try
        {
            claimed = await _parcelRepository.TryClaimCargoRecoveryReturnAsync(
                command.IdempotencyKey,
                command.ParcelId,
                command.OperatorId,
                command.ReturnedByUserId,
                reason,
                command.IsStatusOverride,
                _clock.UtcNow,
                cancellationToken);
            if (claimed is null)
            {
                await _unitOfWork.RollbackAsync(cancellationToken);
            }
            else
            {
                await _unitOfWork.CommitAsync(cancellationToken);
            }
        }
        catch
        {
            await _unitOfWork.RollbackAsync(cancellationToken);
            throw;
        }

        if (claimed is not null)
        {
            return claimed;
        }

        var concurrent = await _parcelRepository.GetActiveCargoRecoveryOperationAsync(
            command.ParcelId,
            cancellationToken);
        if (concurrent is not null)
        {
            EnsureMatchingReturn(concurrent, sourceTripId);
            return concurrent;
        }

        throw new CodedConflictException(
            "TRIP_CARGO_TRANSFER_CONFLICT",
            "Parcel return lost a concurrent state change.");
    }

    private static void EnsureMatchingReturn(
        ParcelCargoRecoveryOperationSnapshot operation,
        Guid sourceTripId)
    {
        if (operation.OperationType != ParcelCargoRecoveryOperationType.RETURN
            || operation.SourceTripId != sourceTripId)
        {
            throw new CodedConflictException(
                "PARCEL_CARGO_RECOVERY_IN_PROGRESS",
                "Parcel already has a different cargo recovery operation in progress.");
        }
    }
}
