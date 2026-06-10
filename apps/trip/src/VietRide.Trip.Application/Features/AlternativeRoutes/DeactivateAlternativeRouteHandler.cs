using MediatR;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.UnitOfWork;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Abstractions.Repositories;
using VietRide.Trip.Application.Features.Stops;

namespace VietRide.Trip.Application.Features.AlternativeRoutes;

public sealed class DeactivateAlternativeRouteHandler : IRequestHandler<DeactivateAlternativeRouteCommand, Unit>
{
    private readonly IAlternativeRouteRepository alternativeRouteRepository;
    private readonly IIdentityInternalClient identityInternalClient;
    private readonly IUnitOfWork unitOfWork;

    public DeactivateAlternativeRouteHandler(
        IAlternativeRouteRepository alternativeRouteRepository,
        IIdentityInternalClient identityInternalClient,
        IUnitOfWork unitOfWork)
    {
        this.alternativeRouteRepository = alternativeRouteRepository;
        this.identityInternalClient = identityInternalClient;
        this.unitOfWork = unitOfWork;
    }

    public async Task<Unit> Handle(DeactivateAlternativeRouteCommand request, CancellationToken cancellationToken)
    {
        await StopWriteEligibilityGuard.ValidateOperatorCanWriteAsync(identityInternalClient, request.OperatorId, cancellationToken);

        var alternativeRoute = await alternativeRouteRepository.GetOwnedByIdAsync(
            request.OperatorId,
            request.AlternativeRouteId,
            cancellationToken);
        if (alternativeRoute is null)
        {
            throw new CodedNotFoundException("ROUTE_NOT_FOUND", "Alternative route was not found.");
        }

        alternativeRoute.Deactivate();
        alternativeRouteRepository.Update(alternativeRoute);
        await unitOfWork.SaveChangesAsync(cancellationToken);

        return Unit.Value;
    }
}
