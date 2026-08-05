using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Trip.Application.Abstractions.Repositories;

namespace VietRide.Trip.Application.Features.DriverSchedules;

public sealed class DeactivateDriverScheduleHandler
    : IRequestHandler<DeactivateDriverScheduleCommand, DriverScheduleDto>
{
    private readonly IDriverScheduleRepository schedules;
    private readonly IUnitOfWork unitOfWork;

    public DeactivateDriverScheduleHandler(IDriverScheduleRepository schedules, IUnitOfWork unitOfWork)
    {
        this.schedules = schedules;
        this.unitOfWork = unitOfWork;
    }

    public async Task<DriverScheduleDto> Handle(
        DeactivateDriverScheduleCommand request,
        CancellationToken cancellationToken)
    {
        var schedule = await schedules.GetByIdAsync(request.DriverScheduleId, cancellationToken);
        if (schedule is null || schedule.OperatorId != request.OperatorId)
            throw new CodedNotFoundException("DRIVER_SCHEDULE_NOT_FOUND", "Driver schedule was not found.");

        if (schedule.IsActive)
        {
            schedule.Deactivate();
            schedules.Update(schedule);
            await unitOfWork.SaveChangesAsync(cancellationToken);
        }

        return DriverScheduleMapper.ToDto(schedule);
    }
}
