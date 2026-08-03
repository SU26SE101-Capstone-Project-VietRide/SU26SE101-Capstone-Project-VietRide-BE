using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Stops;

namespace VietRide.Trip.Application.Features.FareSurcharges;

public sealed class UpdateFareSurchargePeriodCommandHandler
    : IRequestHandler<UpdateFareSurchargePeriodCommand, FareSurchargePeriodDto>
{
    private readonly IClock _clock;
    private readonly IIdentityInternalClient _identity;
    private readonly IOperatorFareSurchargePeriodRepository _periods;
    private readonly IUnitOfWork _unitOfWork;

    public UpdateFareSurchargePeriodCommandHandler(
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
        UpdateFareSurchargePeriodCommand request,
        CancellationToken cancellationToken)
    {
        await StopWriteEligibilityGuard.ValidateOperatorCanWriteAsync(
            _identity,
            request.OperatorId,
            cancellationToken);
        var period = await _periods.GetOwnedByIdAsync(request.OperatorId, request.PeriodId, cancellationToken)
            ?? throw new CodedNotFoundException(
                "FARE_SURCHARGE_PERIOD_NOT_FOUND",
                "Fare surcharge period was not found.");

        var name = request.Name ?? period.Name;
        var startDate = request.StartDate ?? period.StartDate;
        var endDate = request.EndDate ?? period.EndDate;
        var surchargePercent = request.SurchargePercent ?? period.SurchargePercent;
        var isActive = request.IsActive ?? period.IsActive;
        if (endDate < startDate)
        {
            throw new ValidationException(
                "End date cannot be before start date.",
                [new ValidationError("endDate", "End date cannot be before start date.")]);
        }

        await FareSurchargeOverlapGuard.EnsureAvailableAsync(
            _periods,
            request.OperatorId,
            startDate,
            endDate,
            period.Id,
            isActive,
            cancellationToken);

        period.Update(name, startDate, endDate, surchargePercent, isActive);
        _periods.Update(period);
        await _unitOfWork.SaveChangesAsync(cancellationToken);

        return FareSurchargePeriodDto.FromEntity(period, FareSurchargeDate.Today(_clock.UtcNow));
    }
}
