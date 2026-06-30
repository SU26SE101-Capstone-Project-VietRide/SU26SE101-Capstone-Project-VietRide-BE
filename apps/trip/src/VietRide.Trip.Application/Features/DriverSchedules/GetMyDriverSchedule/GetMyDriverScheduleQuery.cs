using VietRide.Shared.Application.Cqrs;

namespace VietRide.Trip.Application.Features.DriverSchedules.GetMyDriverSchedule;

public sealed record GetMyDriverScheduleQuery(
    Guid UserId,
    DateOnly? From,
    DateOnly? To) : IQuery<GetMyDriverScheduleResult>;
