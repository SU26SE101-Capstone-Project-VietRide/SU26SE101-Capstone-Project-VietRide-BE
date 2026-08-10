using MediatR;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.Time;
using VietRide.Trip.Application.Abstractions.Repositories;

namespace VietRide.Trip.Application.Features.DriverSchedules.GetMyDriverSchedule;

public sealed class GetMyDriverScheduleHandler
    : IRequestHandler<GetMyDriverScheduleQuery, GetMyDriverScheduleResult>
{
    private const int DefaultWindowDays = 14;

    private readonly IClock clock;
    private readonly ITripRepository tripRepository;

    public GetMyDriverScheduleHandler(IClock clock, ITripRepository tripRepository)
    {
        this.clock = clock;
        this.tripRepository = tripRepository;
    }

    public Task<GetMyDriverScheduleResult> Handle(
        GetMyDriverScheduleQuery request,
        CancellationToken cancellationToken)
    {
        var today = BusinessTime.ToLocalDate(clock.UtcNow);
        var from = request.From ?? today;
        var to = request.To ?? today.AddDays(DefaultWindowDays);
        var range = BusinessTime.GetUtcRange(from, to);

        var trips = tripRepository.QueryNoTracking()
            .Where(trip =>
                (trip.DriverUserId == request.UserId || trip.AssistantUserId == request.UserId)
                && trip.DepartureDateTime >= range.FromUtc
                && trip.DepartureDateTime < range.ToUtcExclusive)
            .OrderBy(trip => trip.DepartureDateTime)
            .ThenBy(trip => trip.Id)
            .Select(trip => new GetMyDriverScheduleDto(
                trip.Id,
                trip.OperatorId,
                trip.RouteId,
                trip.VehicleId,
                trip.DepartureDateTime,
                trip.EstimatedArrivalTime,
                trip.Status.ToString(),
                trip.DriverUserId == request.UserId ? "DRIVER" : "ASSISTANT"))
            .ToList();

        return Task.FromResult(new GetMyDriverScheduleResult(from, to, trips));
    }

}
