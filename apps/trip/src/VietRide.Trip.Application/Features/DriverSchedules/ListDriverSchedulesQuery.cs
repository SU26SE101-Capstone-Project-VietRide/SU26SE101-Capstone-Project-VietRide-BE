using MediatR;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Trip.Application.Features.DriverSchedules;

public sealed record ListDriverSchedulesQuery(
    Guid OperatorId,
    int? Page,
    int? PageSize,
    Guid? RouteId,
    Guid? DriverUserId,
    bool? IsActive,
    string? Search = null,
    Guid? VehicleTypeId = null,
    int? DayOfWeek = null,
    TimeOnly? DepartureFrom = null,
    TimeOnly? DepartureTo = null,
    DateOnly? EffectiveAt = null,
    Guid? AssistantUserId = null,
    string? SortBy = null,
    string? SortDir = null)
    : IRequest<PagedResult<DriverScheduleDetailDto>>;
