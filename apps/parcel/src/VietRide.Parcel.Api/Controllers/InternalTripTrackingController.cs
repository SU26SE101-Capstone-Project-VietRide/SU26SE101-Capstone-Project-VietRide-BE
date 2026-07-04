using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Parcel.Application.Features.Parcels.TrackingAuthorization;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Web.Authentication;

namespace VietRide.Parcel.Api.Controllers;

[ApiController]
[Route("internal/v1/trips")]
[Authorize(AuthenticationSchemes = InternalJwtAuthenticationExtensions.Scheme)]
public sealed class InternalTripTrackingController : ControllerBase
{
    private readonly IMediator _mediator;

    public InternalTripTrackingController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("{tripId:guid}/tracking-authorization/parcels")]
    [ProducesResponseType(typeof(ApiResponse<ParcelTrackingAuthorizationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ApiResponse<ParcelTrackingAuthorizationResponse>>> GetTrackingAuthorizationAsync(
        Guid tripId,
        [FromQuery] Guid? userId,
        [FromQuery] string? role,
        [FromQuery] Guid? operatorId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetParcelTrackingAuthorizationQuery(tripId, userId, role, operatorId),
            cancellationToken);

        return Ok(ApiResponse<ParcelTrackingAuthorizationResponse>.Ok(result, ApiMeta.Create(HttpContext.TraceIdentifier)));
    }
}
