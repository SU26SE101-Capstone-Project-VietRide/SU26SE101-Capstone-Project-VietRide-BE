using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using VietRide.Parcel.Api.Controllers.Requests;
using VietRide.Parcel.Api.Filters;
using VietRide.Parcel.Application.Features.Parcels.AssistantActions;
using VietRide.Parcel.Application.Features.Parcels.AssistantTripParcels;
using VietRide.Parcel.Application.Features.Parcels.CheckIn;
using VietRide.Parcel.Application.Features.Parcels.Deliver;
using VietRide.Parcel.Application.Features.Parcels.ManualConfirmDelivery;
using VietRide.Parcel.Application.Features.Parcels.MarkLoaded;
using VietRide.Parcel.Application.Features.Parcels.QrScan;
using VietRide.Parcel.Application.Features.Parcels.Reweigh;
using VietRide.Parcel.Application.Features.Parcels.Unload;
using VietRide.Parcel.Application.Features.Reliability.CustodyException;
using VietRide.Parcel.Application.Features.Reliability.CustodyScan;
using VietRide.Parcel.Application.Features.Reliability.Reconciliation;
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
    [ProducesResponseType(typeof(ApiResponse<AssistantTripParcelManifestResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<AssistantTripParcelManifestResponse>> GetByTripAsync(
        Guid tripId,
        [FromQuery] Guid? stopId = null,
        [FromQuery] string? status = null,
        [FromQuery] bool? hasException = null,
        [FromQuery] string? search = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var userId = CurrentUserClaims.GetUserId(User);
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required.");

        var result = await _mediator.Send(
            new GetAssistantTripParcelsQuery(
                tripId,
                userId,
                operatorId,
                page,
                pageSize,
                stopId,
                status,
                hasException,
                search),
            cancellationToken);

        return Ok(result);
    }

    [HttpPost("~/v1/assistant/trips/{tripId:guid}/parcels/qr-scan")]
    [SkipIdempotency("QR scan resolves a parcel code without mutating state.")]
    [ProducesResponseType(typeof(ApiResponse<AssistantParcelActionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<AssistantParcelActionResponse>> ScanQrAsync(
        Guid tripId,
        [FromBody] ScanParcelCodeForTripRequest request,
        CancellationToken cancellationToken = default)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required.");

        var result = await _mediator.Send(
            new ScanParcelCodeForTripQuery(
                tripId,
                request.ParcelCode,
                CurrentUserClaims.GetUserId(User),
                operatorId),
            cancellationToken);

        return Ok(await GetActionStateAsync(result.ParcelId, operatorId, false, null, cancellationToken));
    }

    [HttpPost("{parcelId:guid}/load")]
    [RequireIdempotency]
    [ProducesResponseType(typeof(ApiResponse<AssistantParcelActionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<AssistantParcelActionResponse>> LoadAsync(
        Guid parcelId,
        [FromBody] LoadParcelRequest request,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserClaims.GetUserId(User);
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required.");

        await _mediator.Send(
            new MarkParcelLoadedCommand(
                parcelId,
                request.TripId,
                request.ParcelCode,
                userId,
                operatorId,
                ReadIdempotencyKey(parcelId)),
            cancellationToken);

        return Ok(await GetActionStateAsync(parcelId, operatorId, true, null, cancellationToken));
    }

    [HttpPost("{parcelId:guid}/check-in")]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(ApiResponse<AssistantParcelActionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<AssistantParcelActionResponse>> CheckInAsync(
        Guid parcelId,
        [FromBody] CheckInParcelRequest request,
        CancellationToken cancellationToken)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required.");
        await _mediator.Send(
            new CheckInParcelCommand(
                parcelId,
                request.TripId,
                request.ParcelCode,
                request.PhotoUrls,
                CurrentUserClaims.GetUserId(User),
                operatorId),
            cancellationToken);
        return Ok(await GetActionStateAsync(parcelId, operatorId, true, null, cancellationToken));
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
        var role = CurrentUserClaims.GetRole(User);

        var result = await _mediator.Send(
            new ManualConfirmDeliveryCommand(
                parcelId,
                userId,
                operatorId,
                request.ResolveNote(),
                role),
            cancellationToken);

        return Ok(result);
    }
    [HttpPost("{parcelId:guid}/unload")]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(ApiResponse<AssistantParcelActionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<AssistantParcelActionResponse>> UnloadAsync(
        Guid parcelId,
        [FromBody] UnloadParcelRequest request,
        CancellationToken cancellationToken)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required.");
        var userId = CurrentUserClaims.GetUserId(User);

        await _mediator.Send(
            new UnloadParcelCommand(
                parcelId,
                userId,
                operatorId,
                ReadIdempotencyKey(parcelId),
                request.ActualLocation.Kind,
                request.ActualLocation.Id,
                request.PhotoUrls,
                request.ParcelCode),
            cancellationToken);

        return Ok(await GetActionStateAsync(parcelId, operatorId, true, null, cancellationToken));
    }

    [HttpPost("{parcelId:guid}/custody-exception")]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(ApiResponse<ReportCustodyExceptionResponse>), StatusCodes.Status202Accepted)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ReportCustodyExceptionResponse>> ReportCustodyExceptionAsync(
        Guid parcelId,
        [FromBody] CustodyExceptionRequest request,
        CancellationToken cancellationToken)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required.");

        var result = await _mediator.Send(
            new ReportCustodyExceptionCommand(
                parcelId,
                CurrentUserClaims.GetUserId(User),
                operatorId,
                CurrentUserClaims.GetRole(User),
                request.IncidentType,
                request.ActualLocationType,
                request.ActualLocationId,
                request.LocationSnapshot,
                request.TemporaryExceptionTag,
                request.Description,
                request.ObservedWeightKg,
                request.EvidenceUrls,
                request.Reason,
                ReadIdempotencyKey(parcelId)),
            cancellationToken);

        return Accepted(result);
    }

    [HttpPost("{parcelId:guid}/custody-scan")]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(ApiResponse<AssistantParcelActionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<AssistantParcelActionResponse>> RecordCustodyScanAsync(
        Guid parcelId,
        [FromBody] ParcelCustodyScanRequest request,
        CancellationToken cancellationToken)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required.");
        await _mediator.Send(
            new RecordParcelCustodyScanCommand(
                parcelId,
                operatorId,
                CurrentUserClaims.GetUserId(User),
                CurrentUserClaims.GetRole(User),
                request.ParcelCode,
                request.EventType,
                request.ActualLocationType,
                request.ActualLocationId,
                request.LocationSnapshot,
                request.EvidenceReferences,
                request.Reason,
                ReadIdempotencyKey(parcelId),
                RequireAssignedCrew: true),
            cancellationToken);
        return Ok(await GetActionStateAsync(parcelId, operatorId, true, null, cancellationToken));
    }

    [HttpPost("~/v1/assistant/trips/{tripId:guid}/stops/{stopId:guid}/reconcile")]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(ApiResponse<ReconcileParcelStopResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ReconcileParcelStopResponse>> ReconcileStopAsync(
        Guid tripId,
        Guid stopId,
        [FromBody] ReconcileParcelStopRequest request,
        CancellationToken cancellationToken)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required.");
        var result = await _mediator.Send(
            new ReconcileParcelStopCommand(
                tripId,
                stopId,
                CurrentUserClaims.GetUserId(User),
                operatorId,
                request.ScannedParcelIds ?? Array.Empty<Guid>(),
                request.ManualExceptionParcelIds ?? Array.Empty<Guid>(),
                request.DepartureOverrideReason,
                Guid.Parse(Request.Headers[RequireIdempotencyKeyAttribute.HeaderName].ToString())),
            cancellationToken);
        return Ok(result);
    }

    [HttpPost("{parcelId:guid}/deliver")]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(ApiResponse<AssistantParcelActionResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<AssistantParcelActionResponse>> DeliverAsync(
        Guid parcelId,
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)] DeliverParcelRequest? request,
        CancellationToken cancellationToken)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required.");
        var userId = CurrentUserClaims.GetUserId(User);

        await _mediator.Send(
            new DeliverParcelCommand(parcelId, userId, operatorId, request?.PhotoUrls),
            cancellationToken);

        return Ok(await GetActionStateAsync(parcelId, operatorId, true, null, cancellationToken));
    }

    private Task<AssistantParcelActionResponse> GetActionStateAsync(
        Guid parcelId,
        Guid operatorId,
        bool includeLatestCustodyEvent,
        string? warning,
        CancellationToken cancellationToken)
        => _mediator.Send(
            new GetAssistantParcelActionStateQuery(
                parcelId,
                CurrentUserClaims.GetUserId(User),
                operatorId,
                includeLatestCustodyEvent,
                warning),
            cancellationToken);

    private Guid ReadIdempotencyKey(Guid fallback)
        => Guid.TryParse(
            Request.Headers[RequireIdempotencyKeyAttribute.HeaderName].ToString(),
            out var key)
            ? key
            : fallback;
}
