using MediatR;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Application.Abstractions.Repositories;

namespace VietRide.Trip.Application.Features.FareSurcharges;

public sealed class ListFareSurchargePeriodsQueryHandler
    : IRequestHandler<ListFareSurchargePeriodsQuery, PagedResult<FareSurchargePeriodDto>>
{
    private readonly IClock _clock;
    private readonly IOperatorFareSurchargePeriodRepository _periods;

    public ListFareSurchargePeriodsQueryHandler(
        IOperatorFareSurchargePeriodRepository periods,
        IClock clock)
    {
        _periods = periods;
        _clock = clock;
    }

    public Task<PagedResult<FareSurchargePeriodDto>> Handle(
        ListFareSurchargePeriodsQuery request,
        CancellationToken cancellationToken)
    {
        var page = request.Page ?? 1;
        var pageSize = request.PageSize ?? 20;
        var query = _periods.QueryNoTracking()
            .Where(x => x.OperatorId == request.OperatorId)
            .OrderBy(x => x.StartDate)
            .ThenBy(x => x.Id);
        cancellationToken.ThrowIfCancellationRequested();
        var totalItems = query.LongCount();
        var entities = query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToList();
        var today = FareSurchargeDate.Today(_clock.UtcNow);

        return Task.FromResult(PagedResult<FareSurchargePeriodDto>.Create(
            entities.Select(x => FareSurchargePeriodDto.FromEntity(x, today)).ToArray(),
            page,
            pageSize,
            totalItems));
    }
}
