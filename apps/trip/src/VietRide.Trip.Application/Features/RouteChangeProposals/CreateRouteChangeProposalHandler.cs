using MediatR;
using VietRide.Trip.Application.Abstractions.Services;

namespace VietRide.Trip.Application.Features.RouteChangeProposals;

public sealed class CreateRouteChangeProposalHandler : IRequestHandler<CreateRouteChangeProposalCommand, RouteChangeProposalDto>
{
    private readonly IRouteChangeProposalService service;
    public CreateRouteChangeProposalHandler(IRouteChangeProposalService service) => this.service = service;
    public Task<RouteChangeProposalDto> Handle(CreateRouteChangeProposalCommand request, CancellationToken cancellationToken)
        => service.CreateAsync(request.TripId, request.UserId, request.Type, request.AlternativeRouteId, request.CustomRoute, request.IncidentId, request.Reason, cancellationToken);
}
