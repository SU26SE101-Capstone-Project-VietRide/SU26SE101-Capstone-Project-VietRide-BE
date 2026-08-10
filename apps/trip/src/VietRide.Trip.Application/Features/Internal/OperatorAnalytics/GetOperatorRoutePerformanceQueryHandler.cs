using System.Globalization;
using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Time;
using VietRide.Trip.Application.Abstractions.Repositories;

namespace VietRide.Trip.Application.Features.Internal.OperatorAnalytics;

public sealed class GetOperatorRoutePerformanceQueryHandler
    : IRequestHandler<GetOperatorRoutePerformanceQuery, IReadOnlyList<OperatorRoutePerformanceResponse>>
{
    private readonly IOperatorAnalyticsRepository repository;

    public GetOperatorRoutePerformanceQueryHandler(IOperatorAnalyticsRepository repository)
    {
        this.repository = repository;
    }

    public async Task<IReadOnlyList<OperatorRoutePerformanceResponse>> Handle(
        GetOperatorRoutePerformanceQuery request,
        CancellationToken cancellationToken)
    {
        if (request.OperatorId == Guid.Empty)
        {
            throw Validation("operatorId", "operatorId must be a non-empty UUID.");
        }

        var (fromUtc, toUtc) = ParseMonth(request.Month);
        var rows = await repository.GetRoutePerformanceAsync(
            request.OperatorId,
            fromUtc,
            toUtc,
            cancellationToken);

        return rows
            .OrderBy(item => item.RouteName, StringComparer.Ordinal)
            .ThenBy(item => item.RouteId)
            .Select(item => new OperatorRoutePerformanceResponse(
                item.RouteId,
                item.RouteName,
                item.OriginName,
                item.DestinationName,
                item.TripCount,
                item.CompletedTripCount))
            .ToArray();
    }

    private static (DateTimeOffset FromUtc, DateTimeOffset ToUtc) ParseMonth(string? month)
    {
        if (month?.Length != 7
            || !DateOnly.TryParseExact(
                $"{month}-01",
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var firstDay))
        {
            throw Validation("month", "month must use YYYY-MM format.");
        }

        DateOnly nextMonth;
        try
        {
            nextMonth = firstDay.AddMonths(1);
        }
        catch (ArgumentOutOfRangeException)
        {
            throw Validation("month", "month must have a representable following month.");
        }

        var fromUtc = BusinessTime.ToUtc(firstDay, TimeOnly.MinValue);
        var toUtc = BusinessTime.ToUtc(nextMonth, TimeOnly.MinValue);
        return (fromUtc, toUtc);
    }

    private static CodedValidationException Validation(string field, string message)
        => new(
            "VALIDATION_ERROR",
            message,
            [new ValidationError(field, message)]);
}
