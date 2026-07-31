using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Trip.Application.Abstractions.Repositories;

namespace VietRide.Trip.Application.Features.Internal.OperatorAnalytics;

public sealed class GetOperatorVehicleCountsQueryHandler
    : IRequestHandler<GetOperatorVehicleCountsQuery, IReadOnlyList<OperatorVehicleCountResponse>>
{
    private const int MaximumBatchSize = 100;
    private readonly IOperatorAnalyticsRepository repository;

    public GetOperatorVehicleCountsQueryHandler(IOperatorAnalyticsRepository repository)
    {
        this.repository = repository;
    }

    public async Task<IReadOnlyList<OperatorVehicleCountResponse>> Handle(
        GetOperatorVehicleCountsQuery request,
        CancellationToken cancellationToken)
    {
        Validate(request.OperatorIds);

        var counts = await repository.GetVehicleCountsAsync(request.OperatorIds, cancellationToken);
        var countByOperator = counts.ToDictionary(item => item.OperatorId, item => item.VehicleCount);

        return request.OperatorIds
            .OrderBy(operatorId => operatorId)
            .Select(operatorId => new OperatorVehicleCountResponse(
                operatorId,
                countByOperator.GetValueOrDefault(operatorId)))
            .ToArray();
    }

    private static void Validate(IReadOnlyList<Guid>? operatorIds)
    {
        var isInvalid = operatorIds is null
            || operatorIds.Count is < 1 or > MaximumBatchSize
            || operatorIds.Any(operatorId => operatorId == Guid.Empty)
            || operatorIds.Distinct().Count() != operatorIds.Count;

        if (isInvalid)
        {
            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "operatorIds must contain 1 to 100 distinct non-empty UUIDs.",
                [new ValidationError("operatorIds", "operatorIds must contain 1 to 100 distinct non-empty UUIDs.")]);
        }
    }
}
