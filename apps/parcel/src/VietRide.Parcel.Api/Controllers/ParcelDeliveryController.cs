using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Parcel.Api.Controllers.Requests;
using VietRide.Parcel.Api.Filters;
using VietRide.Parcel.Application.Features.Parcels.ConfirmDelivery;
using VietRide.Parcel.Application.Features.Parcels.RejectDelivery;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.Api.Controllers;

[ApiController]
[Route("v1/parcels/delivery")]
[AllowAnonymous]
public sealed class ParcelDeliveryController : ControllerBase
{
    private readonly IMediator _mediator;

    public ParcelDeliveryController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("confirm")]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(ApiResponse<ConfirmDeliveryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ConfirmDeliveryResponse>> ConfirmAsync(
        [FromBody] ConfirmDeliveryRequest request,
        CancellationToken cancellationToken)
    {
        var ip = HttpContext.Connection.RemoteIpAddress?.ToString() ?? string.Empty;
        var result = await _mediator.Send(
            new ConfirmDeliveryCommand(request.Token, ip),
            cancellationToken);

        return Ok(result);
    }

    [HttpPost("reject")]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(ApiResponse<RejectDeliveryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RejectDeliveryResponse>> RejectAsync(
        [FromBody] RejectDeliveryRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new RejectDeliveryCommand(request.Token, request.RejectionReason),
            cancellationToken);

        return Ok(result);
    }
}
