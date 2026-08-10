using MediatR;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Time;

namespace VietRide.Identity.Application.Features.Internal.AdminDashboard;

public sealed class GetAdminDashboardIdentityMetricsQueryHandler
    : IRequestHandler<GetAdminDashboardIdentityMetricsQuery, AdminDashboardIdentityMetricsResponse>
{
    private const int MaximumInclusiveDays = 366;

    private readonly IAdminDashboardIdentityMetricsRepository _repository;

    public GetAdminDashboardIdentityMetricsQueryHandler(
        IAdminDashboardIdentityMetricsRepository repository)
    {
        _repository = repository;
    }

    public async Task<AdminDashboardIdentityMetricsResponse> Handle(
        GetAdminDashboardIdentityMetricsQuery request,
        CancellationToken cancellationToken)
    {
        ValidateRange(request.From, request.To);

        var result = await _repository.GetAsync(
            ToUtcStart(request.From!.Value),
            ToUtcExclusive(request.To!.Value),
            cancellationToken);

        return new AdminDashboardIdentityMetricsResponse(
            result.ActiveUserCount,
            result.ApprovedActiveOperatorIds.Distinct().OrderBy(id => id).ToArray(),
            result.UserRoleCounts
                .OrderBy(item => GetUserRoleOrder(item.Key))
                .ThenBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => new AdminDashboardUserRoleCountResponse(item.Key, item.Count))
                .ToArray(),
            result.OperatorStatusCounts
                .OrderBy(item => GetOperatorStatusOrder(item.Key))
                .ThenBy(item => item.Key, StringComparer.Ordinal)
                .Select(item => new AdminDashboardOperatorStatusCountResponse(item.Key, item.Count))
                .ToArray());
    }

    private static void ValidateRange(DateOnly? from, DateOnly? to)
    {
        if (!from.HasValue || !to.HasValue)
        {
            var errors = new List<ValidationError>();
            if (!from.HasValue)
            {
                errors.Add(new ValidationError("from", "from is required."));
            }
            if (!to.HasValue)
            {
                errors.Add(new ValidationError("to", "to is required."));
            }

            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "from and to are required for Identity dashboard metrics.",
                errors);
        }

        if (from.Value > to.Value)
        {
            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "from must be on or before to.",
                [new ValidationError("from", "from must be on or before to.")]);
        }

        var inclusiveDays = to.Value.DayNumber - from.Value.DayNumber + 1;
        if (inclusiveDays > MaximumInclusiveDays)
        {
            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "Identity dashboard metrics range cannot exceed 366 inclusive days.",
                [new ValidationError("to", "The inclusive date range cannot exceed 366 days.")]);
        }
    }

    private static DateTimeOffset ToUtcStart(DateOnly date)
        => date == DateOnly.MinValue
            ? DateTimeOffset.MinValue
            : BusinessTime.ToUtc(date, TimeOnly.MinValue);

    private static DateTimeOffset ToUtcExclusive(DateOnly date)
        => date == DateOnly.MaxValue
            ? DateTimeOffset.MaxValue
            : BusinessTime.ToUtc(date.AddDays(1), TimeOnly.MinValue);

    private static int GetUserRoleOrder(string role)
        => Enum.TryParse<UserRole>(role, ignoreCase: false, out var parsed)
            ? (int)parsed
            : int.MaxValue;

    private static int GetOperatorStatusOrder(string status)
        => Enum.TryParse<OperatorRegistrationStatus>(status, ignoreCase: false, out var parsed)
            ? (int)parsed
            : int.MaxValue;
}
