using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Parcel.Api.Controllers.Requests;
using VietRide.Parcel.Api.Filters;
using VietRide.Parcel.Application.Features.Parcels.AccessCheck;
using VietRide.Parcel.Application.Features.Parcels.InternalDetail;
using VietRide.Parcel.Application.Features.Parcels.MarkLoaded;
using VietRide.Parcel.Application.Features.Parcels.OperationalRecovery;
using VietRide.Parcel.Application.Features.Parcels.TripCancellationImpact;
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
            new MarkParcelLoadedCommand(
                parcelId,
                request.TripId,
                request.ParcelCode,
                request.ConfirmedByUserId,
                null,
                Guid.TryParse(
                    Request.Headers[RequireIdempotencyKeyAttribute.HeaderName].ToString(),
                    out var operationId)
                    ? operationId
                    : parcelId),
            cancellationToken);

        return Ok(result);
    }

    [HttpPost("{parcelId:guid}/confirm-transfer")]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(ApiResponse<OperationalParcelResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<OperationalParcelResponse>> ConfirmTransferAsync(
        Guid parcelId,
        [FromBody] ConfirmTransferRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new ConfirmTransferCommand(
                parcelId,
                request.ParcelCode,
                request.ConfirmedByUserId,
                Guid.Parse(Request.Headers[RequireIdempotencyKeyAttribute.HeaderName].ToString()),
                ExpectedTargetTripId: request.TargetTripId,
                RequireCrewAuthorization: false),
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

    [HttpGet("payment-context/{referenceType}/{referenceId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<ParcelPaymentContextSnapshot>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ParcelPaymentContextSnapshot>> GetPaymentContextSnapshotAsync(
        string referenceType,
        Guid referenceId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetParcelPaymentContextQuery(referenceType, referenceId),
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

    [HttpGet("trips/{tripId:guid}/cancel-impact")]
    [ProducesResponseType(typeof(ApiResponse<TripCancellationImpactResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<TripCancellationImpactResponse>> GetTripCancellationImpactAsync(
        Guid tripId,
        [FromQuery] Guid operatorId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetTripCancellationImpactQuery(tripId, operatorId),
            cancellationToken);
        return Ok(result);
    }
}
