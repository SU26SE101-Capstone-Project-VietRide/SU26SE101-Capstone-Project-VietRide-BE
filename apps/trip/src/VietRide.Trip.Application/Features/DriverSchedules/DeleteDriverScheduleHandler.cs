using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Trip.Application.Abstractions.Repositories;

namespace VietRide.Trip.Application.Features.DriverSchedules;

public sealed class DeleteDriverScheduleHandler
    : IRequestHandler<DeleteDriverScheduleCommand, IReadOnlyDictionary<string, bool>>
{
    private readonly IDriverScheduleRepository schedules;
    private readonly ITripRepository trips;
    private readonly IUnitOfWork unitOfWork;
    private readonly IClock clock;

    public DeleteDriverScheduleHandler(
        IDriverScheduleRepository schedules,
        ITripRepository trips,
        IUnitOfWork unitOfWork,
        IClock clock)
    {
        this.schedules = schedules;
        this.trips = trips;
        this.unitOfWork = unitOfWork;
        this.clock = clock;
    }

    public async Task<IReadOnlyDictionary<string, bool>> Handle(
        DeleteDriverScheduleCommand request,
        CancellationToken cancellationToken)
    {
        var schedule = await schedules.GetByIdAsync(request.DriverScheduleId, cancellationToken);
        if (schedule is null || schedule.OperatorId != request.OperatorId)
            throw new CodedNotFoundException("DRIVER_SCHEDULE_NOT_FOUND", "Driver schedule was not found.");

        var tripCount = trips.QueryNoTracking().Count(trip => trip.DriverScheduleId == schedule.Id);
        if (tripCount > 0)
        {
            throw new CodedConflictException(
                "SCHEDULE_HAS_TRIPS",
                "A DriverSchedule with generated Trips cannot be deleted.",
                [new ValidationError("tripCount", tripCount.ToString(System.Globalization.CultureInfo.InvariantCulture))]);
        }

        schedule.SoftDelete(clock.UtcNow);
        schedules.Update(schedule);
        await unitOfWork.SaveChangesAsync(cancellationToken);
        return new Dictionary<string, bool> { ["deleted"] = true };
    }
}
