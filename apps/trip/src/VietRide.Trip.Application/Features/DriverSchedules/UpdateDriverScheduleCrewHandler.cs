using MediatR;

namespace VietRide.Trip.Application.Features.DriverSchedules;

/// <summary>
/// Deprecated one-release alias. All domain behavior belongs to the canonical full update handler.
/// </summary>
public sealed class UpdateDriverScheduleCrewHandler
    : IRequestHandler<UpdateDriverScheduleCrewCommand, DriverScheduleDto>
{
    private readonly ISender sender;

    public UpdateDriverScheduleCrewHandler(ISender sender)
    {
        this.sender = sender;
    }

    public Task<DriverScheduleDto> Handle(
        UpdateDriverScheduleCrewCommand request,
        CancellationToken cancellationToken) =>
        sender.Send(
            new UpdateDriverScheduleCommand(
                request.OperatorId,
                request.DriverScheduleId,
                request.ActorUserId,
                request.RequestId,
                UpdateDriverScheduleCommand.AllPending,
                DepartureTimeSpecified: false,
                DepartureTime: null,
                DayOfWeekSpecified: false,
                DayOfWeek: null,
                DriverUserIdSpecified: true,
                DriverUserId: request.DriverUserId,
                AssistantUserIdSpecified: true,
                AssistantUserId: request.AssistantUserId,
                VehicleIdSpecified: false,
                VehicleId: null,
                ValidUntilSpecified: false,
                ValidUntil: null,
                IsActiveSpecified: false,
                IsActive: null),
            cancellationToken);
}
