using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Parcel.Application.Features.Parcels.OperatorStats;

public sealed class GetOperatorParcelStatsQueryHandler
    : IRequestHandler<GetOperatorParcelStatsQuery, OperatorParcelStatsResponse>
{
    private const string StatusGroup = "status";
    private const string RouteGroup = "route";
    private const int DefaultRouteLimit = 10;
    private const int MaximumRouteLimit = 100;
    private const int MaximumInclusiveDays = 366;
    private static readonly TimeSpan IctOffset = TimeSpan.FromHours(7);
    private static readonly HashSet<string> SupportedGroups = new(StringComparer.OrdinalIgnoreCase)
    {
        StatusGroup,
        RouteGroup,
    };

    private readonly IOperatorParcelStatsRepository _repository;

    public GetOperatorParcelStatsQueryHandler(IOperatorParcelStatsRepository repository)
    {
        _repository = repository;
    }

    public async Task<OperatorParcelStatsResponse> Handle(
        GetOperatorParcelStatsQuery request,
        CancellationToken cancellationToken)
    {
        Validate(request);

        var groupBy = request.GroupBy!.ToLowerInvariant();
        var routeLimit = Math.Clamp(request.Limit ?? DefaultRouteLimit, 1, MaximumRouteLimit);
        var fromUtc = ToUtcStart(request.From!.Value);
        var toUtcExclusive = ToUtcExclusive(request.To!.Value);
        var result = await _repository.GetAsync(
            request.OperatorId,
            fromUtc,
            toUtcExclusive,
            groupBy,
            routeLimit,
            cancellationToken);

        var items = groupBy == StatusGroup
            ? result.Buckets.Select(bucket => new OperatorParcelStatsItemResponse(
                bucket.Key,
                bucket.Count,
                RouteId: null,
                RouteName: null,
                ParcelCount: null)).ToList()
            : result.Buckets.Select(bucket => new OperatorParcelStatsItemResponse(
                Key: null,
                Count: null,
                bucket.RouteId,
                bucket.RouteName,
                bucket.Count)).ToList();

        return new OperatorParcelStatsResponse(items, result.TotalParcels);
    }

    private static void Validate(GetOperatorParcelStatsQuery request)
    {
        if (!SupportedGroups.Contains(request.GroupBy ?? string.Empty))
        {
            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "Parcel stats supports groupBy=status or groupBy=route.",
                [new ValidationError("groupBy", "Only 'status' or 'route' is supported.")]);
        }

        if (!request.From.HasValue || !request.To.HasValue)
        {
            var errors = new List<ValidationError>();
            if (!request.From.HasValue)
            {
                errors.Add(new ValidationError("from", "from is required."));
            }
            if (!request.To.HasValue)
            {
                errors.Add(new ValidationError("to", "to is required."));
            }

            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "from and to are required for Parcel stats.",
                errors);
        }

        if (request.From.Value > request.To.Value)
        {
            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "from must be on or before to.",
                [new ValidationError("from", "from must be on or before to.")]);
        }

        var inclusiveDays = request.To.Value.DayNumber - request.From.Value.DayNumber + 1;
        if (inclusiveDays > MaximumInclusiveDays)
        {
            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "Parcel stats range cannot exceed 366 inclusive days.",
                [new ValidationError("to", "The inclusive date range cannot exceed 366 days.")]);
        }
    }

    private static DateTimeOffset ToUtcStart(DateOnly date)
    {
        if (date == DateOnly.MinValue)
        {
            return DateTimeOffset.MinValue;
        }

        return new DateTimeOffset(date.ToDateTime(TimeOnly.MinValue), IctOffset).ToUniversalTime();
    }

    private static DateTimeOffset ToUtcExclusive(DateOnly date)
    {
        if (date == DateOnly.MaxValue)
        {
            return DateTimeOffset.MaxValue;
        }

        return new DateTimeOffset(date.AddDays(1).ToDateTime(TimeOnly.MinValue), IctOffset).ToUniversalTime();
    }
}
