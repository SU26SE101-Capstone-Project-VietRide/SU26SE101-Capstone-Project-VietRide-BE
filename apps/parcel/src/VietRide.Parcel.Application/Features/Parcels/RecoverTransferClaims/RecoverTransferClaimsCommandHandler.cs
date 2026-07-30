using MediatR;
using Microsoft.Extensions.Logging;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Application.Features.Parcels.OperationalRecovery;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.Application.Features.Parcels.RecoverTransferClaims;

public sealed class RecoverTransferClaimsCommandHandler
    : IRequestHandler<RecoverTransferClaimsCommand, int>
{
    private static readonly TimeSpan StaleClaimAge = TimeSpan.FromMinutes(5);
    private const int MaxBatch = 100;

    private readonly IParcelRepository _parcelRepository;
    private readonly IMediator _mediator;
    private readonly IClock _clock;
    private readonly ILogger<RecoverTransferClaimsCommandHandler> _logger;

    public RecoverTransferClaimsCommandHandler(
        IParcelRepository parcelRepository,
        IMediator mediator,
        IClock clock,
        ILogger<RecoverTransferClaimsCommandHandler> logger)
    {
        _parcelRepository = parcelRepository;
        _mediator = mediator;
        _clock = clock;
        _logger = logger;
    }

    public async Task<int> Handle(
        RecoverTransferClaimsCommand request,
        CancellationToken cancellationToken)
    {
        var candidates = await _parcelRepository.GetStaleTransferConfirmationClaimsAsync(
            _clock.UtcNow.Subtract(StaleClaimAge),
            MaxBatch,
            cancellationToken);
        var recovered = 0;

        foreach (var candidate in candidates)
        {
            if (candidate.ClaimId is null
                || candidate.ClaimedByUserId is null
                || candidate.TargetTripId is null)
            {
                _logger.LogError(
                    "Parcel {ParcelId} has an incomplete durable transfer claim.",
                    candidate.ParcelId);
                continue;
            }

            try
            {
                await _mediator.Send(
                    new ConfirmTransferCommand(
                        candidate.ParcelId,
                        candidate.ParcelCode,
                        candidate.ClaimedByUserId.Value,
                        candidate.ClaimId.Value,
                        ExpectedTargetTripId: candidate.TargetTripId,
                        RequireCrewAuthorization: false),
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
                    "Deferred transfer-claim recovery for Parcel {ParcelId}.",
                    candidate.ParcelId);
            }
        }

        return recovered;
    }
}
