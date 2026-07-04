using MediatR;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Trip.Application.Abstractions.Repositories;

namespace VietRide.Trip.Application.Features.DriverSchedules.GetMyDriverSchedule;

public sealed class GetMyDriverScheduleHandler
    : IRequestHandler<GetMyDriverScheduleQuery, GetMyDriverScheduleResult>
{
    private static readonly TimeSpan IctOffset = TimeSpan.FromHours(7);
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
        var today = DateOnly.FromDateTime(clock.UtcNow.ToOffset(IctOffset).DateTime);
        var from = request.From ?? today;
        var to = request.To ?? today.AddDays(DefaultWindowDays);
        var startUtc = ToUtcBoundary(from);
        var endExclusiveUtc = ToUtcBoundary(to.AddDays(1));

        var trips = tripRepository.QueryNoTracking()
            .Where(trip =>
                (trip.DriverUserId == request.UserId || trip.AssistantUserId == request.UserId)
                && trip.DepartureDateTime >= startUtc
                && trip.DepartureDateTime < endExclusiveUtc)
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

    private static DateTimeOffset ToUtcBoundary(DateOnly date) =>
        new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), IctOffset).ToUniversalTime();
}
