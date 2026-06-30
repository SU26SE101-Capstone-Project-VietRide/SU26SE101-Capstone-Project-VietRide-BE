using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Parcel.Api.Controllers.Requests;
using VietRide.Parcel.Api.Filters;
using VietRide.Parcel.Application.Features.Parcels.AccessCheck;
using VietRide.Parcel.Application.Features.Parcels.InternalDetail;
using VietRide.Parcel.Application.Features.Parcels.MarkLoaded;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Web.Authentication;

namespace VietRide.Parcel.Api.Controllers;

[ApiController]
[Route("internal/v1/parcels")]
[Authorize(AuthenticationSchemes = InternalJwtAuthenticationExtensions.Scheme)]
public sealed class InternalParcelsController : ControllerBase
{
    private readonly IMediator _mediator;

    public InternalParcelsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("{parcelId:guid}/mark-loaded")]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(ApiResponse<MarkParcelLoadedResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<MarkParcelLoadedResponse>> MarkLoadedAsync(
        Guid parcelId,
        [FromBody] MarkParcelLoadedRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new MarkParcelLoadedCommand(parcelId, request.TripId, request.ParcelCode),
            cancellationToken);

        return Ok(result);
    }

    [HttpGet("{parcelId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<InternalParcelResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<InternalParcelResponse>> GetAsync(
        Guid parcelId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetInternalParcelQuery(parcelId),
            cancellationToken);

        return Ok(result);
    }

    [HttpGet("{parcelId:guid}/access-check")]
    [ProducesResponseType(typeof(ApiResponse<ParcelAccessCheckResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ParcelAccessCheckResponse>> AccessCheckAsync(
        Guid parcelId,
        [FromQuery] Guid? userId,
        [FromQuery] Guid? operatorId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetParcelAccessCheckQuery(parcelId, userId, operatorId),
            cancellationToken);

        return Ok(result);
    }
}
