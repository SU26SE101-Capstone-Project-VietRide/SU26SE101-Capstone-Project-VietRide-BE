using MediatR;
using VietRide.Shared.Application.Behaviors;

namespace VietRide.Trip.Application.Features.DriverSchedules;

[SkipTransaction]
public sealed record UpdateDriverScheduleCommand(
    Guid OperatorId,
    Guid DriverScheduleId,
    Guid ActorUserId,
    string RequestId,
    string ApplyTo,
    bool DepartureTimeSpecified,
    TimeOnly? DepartureTime,
    bool DayOfWeekSpecified,
    IReadOnlyList<int>? DayOfWeek,
    bool DriverUserIdSpecified,
    Guid? DriverUserId,
    bool AssistantUserIdSpecified,
    Guid? AssistantUserId,
    bool VehicleIdSpecified,
    Guid? VehicleId,
    bool ValidUntilSpecified,
    DateOnly? ValidUntil,
    bool IsActiveSpecified,
    bool? IsActive) : IRequest<DriverScheduleDto>
{
    public const string FutureOnly = "FUTURE_ONLY";
    public const string AllPending = "ALL_PENDING";
}
