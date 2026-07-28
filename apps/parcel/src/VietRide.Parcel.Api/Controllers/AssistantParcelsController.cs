using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Parcel.Api.Controllers.Requests;
using VietRide.Parcel.Api.Filters;
using VietRide.Parcel.Application.Features.Parcels.AssistantTripParcels;
using VietRide.Parcel.Application.Features.Parcels.CheckIn;
using VietRide.Parcel.Application.Features.Parcels.Deliver;
using VietRide.Parcel.Application.Features.Parcels.ManualConfirmDelivery;
using VietRide.Parcel.Application.Features.Parcels.MarkLoaded;
using VietRide.Parcel.Application.Features.Parcels.Reweigh;
using VietRide.Parcel.Application.Features.Parcels.Unload;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Web.Idempotency;

namespace VietRide.Parcel.Api.Controllers;

[ApiController]
[Route("v1/assistant/parcels")]
[Authorize(Roles = "ASSISTANT")]
public sealed class AssistantParcelsController : ControllerBase
{
    private readonly IMediator _mediator;

    public AssistantParcelsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("~/v1/assistant/trips/{tripId:guid}/parcels")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<AssistantTripParcelResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<PagedResult<AssistantTripParcelResponse>>> GetByTripAsync(
        Guid tripId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var userId = CurrentUserClaims.GetUserId(User);
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required.");

        var result = await _mediator.Send(
            new GetAssistantTripParcelsQuery(tripId, userId, operatorId, page, pageSize),
            cancellationToken);

        return Ok(result);
    }

    [HttpPost("{parcelId:guid}/load")]
    [RequireIdempotency]
    [ProducesResponseType(typeof(ApiResponse<MarkParcelLoadedResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<MarkParcelLoadedResponse>> LoadAsync(
        Guid parcelId,
        [FromBody] LoadParcelRequest request,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserClaims.GetUserId(User);
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required.");

        var result = await _mediator.Send(
            new MarkParcelLoadedCommand(
                parcelId,
                request.TripId,
                request.ParcelCode,
                userId,
                operatorId,
                ReadIdempotencyKey(parcelId)),
            cancellationToken);

        return Ok(result);
    }

    [HttpPost("{parcelId:guid}/check-in")]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(ApiResponse<CheckInParcelResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<CheckInParcelResponse>> CheckInAsync(
        Guid parcelId,
        [FromBody] CheckInParcelRequest request,
        CancellationToken cancellationToken)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required.");
        var result = await _mediator.Send(
            new CheckInParcelCommand(
                parcelId,
                request.TripId,
                request.ParcelCode,
                CurrentUserClaims.GetUserId(User),
                operatorId),
            cancellationToken);
        return Ok(result);
    }

    [HttpPost("{parcelId:guid}/reweigh")]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(ApiResponse<ReweighParcelResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ReweighParcelResponse>> ReweighAsync(
        Guid parcelId,
        [FromBody] ReweighParcelRequest request,
        CancellationToken cancellationToken)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required.");

        var result = await _mediator.Send(new ReweighParcelCommand(
            parcelId,
            operatorId,
            CurrentUserClaims.GetUserId(User),
            request.ActualLengthCm,
            request.ActualWidthCm,
            request.ActualHeightCm,
            request.ActualWeightKg,
            Request.Headers[RequireIdempotencyKeyAttribute.HeaderName].ToString()), cancellationToken);

        return Ok(result);
    }

    [HttpPost("{parcelId:guid}/confirm-delivery")]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(ApiResponse<ManualConfirmDeliveryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ManualConfirmDeliveryResponse>> ConfirmDeliveryAsync(
        Guid parcelId,
        [FromBody] ManualConfirmDeliveryRequest request,
        CancellationToken cancellationToken)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required.");
        var userId = CurrentUserClaims.GetUserId(User);

        var result = await _mediator.Send(
            new ManualConfirmDeliveryCommand(parcelId, userId, operatorId, request.Note),
            cancellationToken);

        return Ok(result);
    }
    [HttpPost("{parcelId:guid}/unload")]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(ApiResponse<UnloadParcelResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<UnloadParcelResponse>> UnloadAsync(
        Guid parcelId,
        CancellationToken cancellationToken)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required.");
        var userId = CurrentUserClaims.GetUserId(User);

        var result = await _mediator.Send(
            new UnloadParcelCommand(parcelId, userId, operatorId, ReadIdempotencyKey(parcelId)),
            cancellationToken);

        return Ok(result);
    }

    [HttpPost("{parcelId:guid}/deliver")]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(ApiResponse<DeliverParcelResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<DeliverParcelResponse>> DeliverAsync(
        Guid parcelId,
        CancellationToken cancellationToken)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required.");
        var userId = CurrentUserClaims.GetUserId(User);

        var result = await _mediator.Send(
            new DeliverParcelCommand(parcelId, userId, operatorId),
            cancellationToken);

        return Ok(result);
    }

    private Guid ReadIdempotencyKey(Guid fallback)
        => Guid.TryParse(
            Request.Headers[RequireIdempotencyKeyAttribute.HeaderName].ToString(),
            out var key)
            ? key
            : fallback;
}
