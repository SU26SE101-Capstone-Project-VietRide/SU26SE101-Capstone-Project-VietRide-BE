using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Parcel.Api.Controllers.Requests;
using VietRide.Parcel.Api.Filters;
using VietRide.Parcel.Application.Features.Parcels.AssistantTripParcels;
using VietRide.Parcel.Application.Features.Parcels.ManualConfirmDelivery;
using VietRide.Parcel.Application.Features.Parcels.OperationalRecovery;
using VietRide.Parcel.Application.Features.Parcels.ResendDeliveryEmail;
using VietRide.Parcel.Application.Features.Reliability.CustodyException;
using VietRide.Parcel.Application.Features.Reliability.Reconciliation;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.Api.Controllers;

[ApiController]
[Route("v1/crew/parcels")]
[Authorize(Roles = "DRIVER,ASSISTANT")]
public sealed class CrewParcelsController : ControllerBase
{
    private readonly IMediator _mediator;

    public CrewParcelsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("{parcelId:guid}/custody-exception")]
    [Authorize(Roles = "DRIVER")]
    [ProducesResponseType(typeof(ApiResponse<ReportCustodyExceptionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ReportCustodyExceptionResponse>> GetCustodyExceptionAsync(
        Guid parcelId,
        CancellationToken cancellationToken)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required.");
        return Ok(await _mediator.Send(
            new GetCustodyExceptionRequestQuery(
                parcelId,
                CurrentUserClaims.GetUserId(User),
                operatorId,
                "DRIVER"),
            cancellationToken));
    }

    [HttpPost("{parcelId:guid}/custody-exception-decision")]
    [Authorize(Roles = "DRIVER")]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(ApiResponse<ReportCustodyExceptionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ReportCustodyExceptionResponse>> DecideCustodyExceptionAsync(
        Guid parcelId,
        [FromBody] DecideCustodyExceptionRequest request,
        CancellationToken cancellationToken)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required.");
        return Ok(await _mediator.Send(
            new DecideCustodyExceptionCommand(
                parcelId,
                "PARCEL",
                CurrentUserClaims.GetUserId(User),
                operatorId,
                "DRIVER",
                request.Decision?.Trim().ToUpperInvariant() ?? string.Empty,
                request.Note,
                Guid.Parse(Request.Headers[RequireIdempotencyKeyAttribute.HeaderName].ToString())),
            cancellationToken));
    }

    [HttpGet("~/v1/crew/parcel-stop-departure-approvals/{requestId:guid}")]
    [Authorize(Roles = "DRIVER")]
    [ProducesResponseType(typeof(ApiResponse<ParcelStopDepartureApprovalResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ParcelStopDepartureApprovalResponse>> GetStopDepartureApprovalAsync(
        Guid requestId,
        CancellationToken cancellationToken)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required.");
        return Ok(await _mediator.Send(
            new GetParcelStopDepartureApprovalQuery(
                requestId,
                CurrentUserClaims.GetUserId(User),
                operatorId,
                "DRIVER"),
            cancellationToken));
    }

    [HttpPost("~/v1/crew/parcel-stop-departure-approvals/{requestId:guid}/decision")]
    [Authorize(Roles = "DRIVER")]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(ApiResponse<ParcelStopDepartureApprovalResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ParcelStopDepartureApprovalResponse>> DecideStopDepartureApprovalAsync(
        Guid requestId,
        [FromBody] DecideParcelStopDepartureApprovalRequest request,
        CancellationToken cancellationToken)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required.");
        return Ok(await _mediator.Send(
            new DecideParcelStopDepartureApprovalCommand(
                requestId,
                CurrentUserClaims.GetUserId(User),
                operatorId,
                "DRIVER",
                request.Decision?.Trim().ToUpperInvariant() ?? string.Empty,
                request.Note,
                Guid.Parse(Request.Headers[RequireIdempotencyKeyAttribute.HeaderName].ToString())),
            cancellationToken));
    }

    [HttpGet("~/v1/crew/trips/{tripId:guid}/parcels")]
    [ProducesResponseType(typeof(ApiResponse<AssistantTripParcelManifestResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<AssistantTripParcelManifestResponse>> GetTripManifestAsync(
        Guid tripId,
        [FromQuery] Guid? stopId = null,
        [FromQuery] string? status = null,
        [FromQuery] bool? hasException = null,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required.");
        var result = await _mediator.Send(
            new GetAssistantTripParcelsQuery(
                tripId,
                CurrentUserClaims.GetUserId(User),
                operatorId,
                page,
                pageSize,
                stopId,
                status,
                hasException,
                search,
                CurrentUserClaims.GetRole(User)),
            cancellationToken);
        return Ok(result);
    }

    [HttpPost("{parcelId:guid}/confirm-transfer")]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(ApiResponse<OperationalParcelResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<OperationalParcelResponse>> ConfirmTransferAsync(
        Guid parcelId,
        [FromBody] ConfirmParcelTransferRequest request,
        CancellationToken cancellationToken)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required.");

        var result = await _mediator.Send(
            new ConfirmTransferCommand(
                parcelId,
                request.ParcelCode,
                CurrentUserClaims.GetUserId(User),
                Guid.Parse(Request.Headers[RequireIdempotencyKeyAttribute.HeaderName].ToString()),
                operatorId,
                CurrentUserClaims.GetRole(User)),
            cancellationToken);

        return Ok(result);
    }

    [HttpPost("{parcelId:guid}/manual-confirm")]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(ApiResponse<ManualConfirmDeliveryResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ManualConfirmDeliveryResponse>> ManualConfirmDeliveryAsync(
        Guid parcelId,
        [FromBody] ManualConfirmDeliveryRequest request,
        CancellationToken cancellationToken)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required.");

        var result = await _mediator.Send(
            new ManualConfirmDeliveryCommand(
                parcelId,
                CurrentUserClaims.GetUserId(User),
                operatorId,
                request.ResolveNote(),
                CurrentUserClaims.GetRole(User)),
            cancellationToken);

        return Ok(result);
    }

    [HttpPost("{parcelId:guid}/resend-delivery-email")]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(ApiResponse<ResendDeliveryEmailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ResendDeliveryEmailResponse>> ResendDeliveryEmailAsync(
        Guid parcelId,
        CancellationToken cancellationToken)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required.");

        var result = await _mediator.Send(
            new ResendDeliveryEmailCommand(
                parcelId,
                CurrentUserClaims.GetUserId(User),
                operatorId,
                CurrentUserClaims.GetRole(User)),
            cancellationToken);

        return Ok(result);
    }
}
