using MediatR;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Stops;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.FareSurcharges;

public sealed class CreateFareSurchargePeriodCommandHandler
    : IRequestHandler<CreateFareSurchargePeriodCommand, FareSurchargePeriodDto>
{
    private readonly IClock _clock;
    private readonly IIdentityInternalClient _identity;
    private readonly IOperatorFareSurchargePeriodRepository _periods;
    private readonly IUnitOfWork _unitOfWork;

    public CreateFareSurchargePeriodCommandHandler(
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

    public async Task<FareSurchargePeriodDto> Handle(
        CreateFareSurchargePeriodCommand request,
        CancellationToken cancellationToken)
    {
        await StopWriteEligibilityGuard.ValidateOperatorCanWriteAsync(
            _identity,
            request.OperatorId,
            cancellationToken);
        await FareSurchargeOverlapGuard.EnsureAvailableAsync(
            _periods,
            request.OperatorId,
            request.StartDate,
            request.EndDate,
            null,
            request.IsActive,
            cancellationToken);

        var period = OperatorFareSurchargePeriod.Create(
            request.OperatorId,
            request.Name,
            request.StartDate,
            request.EndDate,
            request.SurchargePercent,
            request.IsActive);
        await _periods.AddAsync(period, cancellationToken);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return FareSurchargePeriodDto.FromEntity(period, FareSurchargeDate.Today(_clock.UtcNow));
    }
}
