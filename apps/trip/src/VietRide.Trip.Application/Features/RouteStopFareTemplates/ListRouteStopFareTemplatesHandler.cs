using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Application.Abstractions.Repositories;

namespace VietRide.Trip.Application.Features.RouteStopFareTemplates;

public sealed class ListRouteStopFareTemplatesHandler : IRequestHandler<ListRouteStopFareTemplatesQuery, PagedResult<RouteStopFareTemplateDto>>
{
    private const int DefaultPage = 1;
    private const int DefaultPageSize = 20;
    private const int MaxPageSize = 100;

    private readonly IRouteRepository routeRepository;
    private readonly IRouteStopFareTemplateRepository fareTemplateRepository;

    public ListRouteStopFareTemplatesHandler(
        IRouteRepository routeRepository,
        IRouteStopFareTemplateRepository fareTemplateRepository)
    {
        this.routeRepository = routeRepository;
        this.fareTemplateRepository = fareTemplateRepository;
    }

    public async Task<PagedResult<RouteStopFareTemplateDto>> Handle(
        ListRouteStopFareTemplatesQuery request,
        CancellationToken cancellationToken)
    {
        var route = await routeRepository.GetOwnedByIdAsync(request.OperatorId, request.RouteId, cancellationToken);
        if (route is null)
        {
            throw new CodedNotFoundException("ROUTE_NOT_FOUND", "Route was not found.");
        }

        var page = request.Page ?? DefaultPage;
        var pageSize = Math.Min(request.PageSize ?? DefaultPageSize, MaxPageSize);
        var query = fareTemplateRepository.QueryNoTracking()
            .Where(template => template.RouteId == request.RouteId);

        var totalItems = query.LongCount();
        var items = query
            .OrderBy(template => template.StopId)
            .ThenBy(template => template.EffectiveFrom)
            .ThenBy(template => template.Id)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(RouteStopFareTemplateMapper.ToDto)
            .ToList();

        return PagedResult<RouteStopFareTemplateDto>.Create(items, page, pageSize, totalItems);
    }
}
