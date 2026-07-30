using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Application.Features.Parcels.OperationalRecovery;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.Application.Features.Parcels.RecoverCargoRecoveryOperations;

public sealed class RecoverCargoRecoveryOperationsCommandHandler
    : IRequestHandler<RecoverCargoRecoveryOperationsCommand, int>
{
    private static readonly TimeSpan StaleOperationAge = TimeSpan.FromMinutes(5);
    private const int MaxBatch = 100;

    private readonly IParcelRepository _parcelRepository;
    private readonly IMediator _mediator;
    private readonly IClock _clock;
    private readonly ILogger<RecoverCargoRecoveryOperationsCommandHandler> _logger;

    public RecoverCargoRecoveryOperationsCommandHandler(
        IParcelRepository parcelRepository,
        IMediator mediator,
        IClock clock,
        ILogger<RecoverCargoRecoveryOperationsCommandHandler> logger)
    {
        _parcelRepository = parcelRepository;
        _mediator = mediator;
        _clock = clock;
        _logger = logger;
    }

    public async Task<int> Handle(
        RecoverCargoRecoveryOperationsCommand request,
        CancellationToken cancellationToken)
    {
        var operations = await _parcelRepository.GetStaleCargoRecoveryOperationsAsync(
            _clock.UtcNow.Subtract(StaleOperationAge),
            MaxBatch,
            cancellationToken);
        var recovered = 0;

        foreach (var operation in operations)
        {
            try
            {
                await _mediator.Send(
                    new ResumeCargoRecoveryOperationCommand(operation.Id),
                    cancellationToken);
                recovered++;
            }
            catch (Exception exception) when (
                exception is CodedNotFoundException
                    or CodedConflictException
                    or CodedValidationException
                    or ParcelDependencyUnavailableException)
            {
                _logger.LogWarning(
                    exception,
                    "Deferred cargo recovery operation {OperationId} for Parcel {ParcelId}.",
                    operation.Id,
                    operation.ParcelId);
            }
        }

        return recovered;
    }
}
