using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Stops;

namespace VietRide.Trip.Application.Features.FareSurcharges;

public sealed class DeleteFareSurchargePeriodCommandHandler : IRequestHandler<DeleteFareSurchargePeriodCommand>
{
    private readonly IClock _clock;
    private readonly IIdentityInternalClient _identity;
    private readonly IOperatorFareSurchargePeriodRepository _periods;
    private readonly IUnitOfWork _unitOfWork;

    public DeleteFareSurchargePeriodCommandHandler(
        IIdentityInternalClient identity,
        IOperatorFareSurchargePeriodRepository periods,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        _identity = identity;
        _periods = periods;
        _unitOfWork = unitOfWork;
        _clock = clock;
    }

    public async Task<Unit> Handle(DeleteFareSurchargePeriodCommand request, CancellationToken cancellationToken)
    {
        await StopWriteEligibilityGuard.ValidateOperatorCanWriteAsync(
            _identity,
            request.OperatorId,
            cancellationToken);
        var period = await _periods.GetOwnedByIdAsync(request.OperatorId, request.PeriodId, cancellationToken)
            ?? throw new CodedNotFoundException(
                "FARE_SURCHARGE_PERIOD_NOT_FOUND",
                "Fare surcharge period was not found.");

        period.SoftDelete(_clock.UtcNow);
        _periods.Update(period);
        await _unitOfWork.SaveChangesAsync(cancellationToken);
        return Unit.Value;
    }
}
