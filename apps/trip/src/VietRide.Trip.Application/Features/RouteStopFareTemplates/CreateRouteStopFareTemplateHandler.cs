using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Stops;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.RouteStopFareTemplates;

public sealed class CreateRouteStopFareTemplateHandler : IRequestHandler<CreateRouteStopFareTemplateCommand, RouteStopFareTemplateDto>
{
    private readonly IIdentityInternalClient identityInternalClient;
    private readonly IRouteRepository routeRepository;
    private readonly IRouteStopFareTemplateRepository fareTemplateRepository;
    private readonly IRouteStopRepository routeStopRepository;
    private readonly IStopRepository stopRepository;
    private readonly IUnitOfWork unitOfWork;

    public CreateRouteStopFareTemplateHandler(
        IIdentityInternalClient identityInternalClient,
        IRouteRepository routeRepository,
        IRouteStopFareTemplateRepository fareTemplateRepository,
        IRouteStopRepository routeStopRepository,
        IStopRepository stopRepository,
        IUnitOfWork unitOfWork)
    {
        this.identityInternalClient = identityInternalClient;
        this.routeRepository = routeRepository;
        this.fareTemplateRepository = fareTemplateRepository;
        this.routeStopRepository = routeStopRepository;
        this.stopRepository = stopRepository;
        this.unitOfWork = unitOfWork;
    }

    public async Task<RouteStopFareTemplateDto> Handle(
        CreateRouteStopFareTemplateCommand request,
        CancellationToken cancellationToken)
    {
        await StopWriteEligibilityGuard.ValidateOperatorCanWriteAsync(
            identityInternalClient,
            request.OperatorId,
            cancellationToken);

        ValidateEffectiveWindow(request.EffectiveFrom, request.EffectiveUntil);

        var route = await routeRepository.GetOwnedByIdAsync(request.OperatorId, request.RouteId, cancellationToken);
        if (route is null)
        {
            throw new CodedNotFoundException("ROUTE_NOT_FOUND", "Route was not found.");
        }

        await ValidateStopBelongsToOperatorAsync(request.OperatorId, request.StopId, cancellationToken);
        await ValidateStopIsConfiguredOnRouteAsync(request.RouteId, request.StopId, cancellationToken);
        await ValidateEffectiveWindowDoesNotOverlapAsync(
            request.RouteId,
            request.StopId,
            request.EffectiveFrom,
            request.EffectiveUntil,
            cancellationToken);

        var template = RouteStopFareTemplate.Create(
            request.RouteId,
            request.StopId,
            Money.FromRaw(request.FareFromThisStop),
            request.EffectiveFrom,
            request.EffectiveUntil);

        await fareTemplateRepository.AddAsync(template, cancellationToken);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return RouteStopFareTemplateMapper.ToDto(template);
    }

    private static void ValidateEffectiveWindow(DateTimeOffset effectiveFrom, DateTimeOffset? effectiveUntil)
    {
        if (effectiveUntil.HasValue && effectiveUntil.Value <= effectiveFrom)
        {
            throw new ValidationException(
                "Effective until must be greater than effective from.",
                [new ValidationError("effectiveUntil", "Effective until must be greater than effective from.")]);
        }
    }

    private async Task ValidateStopBelongsToOperatorAsync(Guid operatorId, Guid stopId, CancellationToken cancellationToken)
    {
        var stop = await stopRepository.GetByIdAsync(stopId, cancellationToken);
        if (stop is null || stop.OperatorId != operatorId || !stop.IsActive || stop.DeletedAt is not null)
        {
            throw new CodedNotFoundException("STOP_NOT_FOUND", "Stop was not found.");
        }
    }

    private async Task ValidateStopIsConfiguredOnRouteAsync(Guid routeId, Guid stopId, CancellationToken cancellationToken)
    {
        if (await routeStopRepository.GetByRouteAndStopAsync(routeId, stopId, cancellationToken) is null)
        {
            throw new ValidationException(
                "Stop must already be configured on this route before adding a fare template.",
                [new ValidationError("stopId", "Stop must already be configured on this route.")]);
        }
    }

    private async Task ValidateEffectiveWindowDoesNotOverlapAsync(
        Guid routeId,
        Guid stopId,
        DateTimeOffset effectiveFrom,
        DateTimeOffset? effectiveUntil,
        CancellationToken cancellationToken)
    {
        if (await fareTemplateRepository.ExistsOverlappingAsync(routeId, stopId, effectiveFrom, effectiveUntil, cancellationToken))
        {
            throw new ValidationException(
                "Fare template effective window overlaps an existing template for this route stop.",
                [
                    new ValidationError("effectiveFrom", "Fare template effective window overlaps an existing template."),
                    new ValidationError("effectiveUntil", "Fare template effective window overlaps an existing template."),
                ]);
        }
    }
}
