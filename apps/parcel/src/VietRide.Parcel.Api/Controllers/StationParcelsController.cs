using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Parcel.Api.Controllers.Requests;
using VietRide.Parcel.Api.Filters;
using VietRide.Parcel.Application.Features.Reliability.CustodyScan;
using VietRide.Parcel.Application.Features.Reliability.UnidentifiedPackages;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.Api.Controllers;

[ApiController]
[Route("v1/stations/parcels")]
[Authorize(Roles = "OPERATOR_ADMIN,OPERATOR_STAFF")]
public sealed class StationParcelsController : ControllerBase
{
    private readonly IMediator _mediator;
    public StationParcelsController(IMediator mediator) => _mediator = mediator;

    [HttpPost("unidentified")]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(ApiResponse<UnidentifiedPackageResponse>), StatusCodes.Status201Created)]
    public async Task<ActionResult<UnidentifiedPackageResponse>> RegisterAsync(
        [FromBody] RegisterUnidentifiedPackageRequest request,
        CancellationToken cancellationToken)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required.");
        var result = await _mediator.Send(
            new RegisterUnidentifiedPackageCommand(
                operatorId,
                CurrentUserClaims.GetUserId(User),
                request.TemporaryExceptionTag,
                request.TripId,
                request.LocationType,
                request.LocationId,
                request.LocationSnapshot,
                request.Description,
                request.ObservedWeightKg,
                request.EvidenceReferences ?? Array.Empty<string>()),
            cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPost("unidentified/{packageId:guid}/match")]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(ApiResponse<UnidentifiedPackageResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<UnidentifiedPackageResponse>> MatchAsync(
        Guid packageId,
        [FromBody] MatchUnidentifiedPackageRequest request,
        CancellationToken cancellationToken)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required.");
        return Ok(await _mediator.Send(
            new MatchUnidentifiedPackageCommand(
                packageId,
                request.ParcelId,
                operatorId,
                CurrentUserClaims.GetUserId(User)),
            cancellationToken));
    }

    [HttpPost("{parcelId:guid}/handoff")]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(ApiResponse<ParcelCustodyScanResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<ParcelCustodyScanResponse>> HandoffAsync(
        Guid parcelId,
        [FromBody] ParcelCustodyScanRequest request,
        CancellationToken cancellationToken)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required.");
        if (!string.Equals(request.EventType, "HANDOFF", StringComparison.OrdinalIgnoreCase)
            && !string.Equals(request.EventType, "RETURNED_TO_STATION", StringComparison.OrdinalIgnoreCase))
            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "Station handoff accepts only HANDOFF or RETURNED_TO_STATION.");

        var key = Guid.TryParse(
            Request.Headers[RequireIdempotencyKeyAttribute.HeaderName].ToString(),
            out var parsedKey)
            ? parsedKey
            : parcelId;
        return Ok(await _mediator.Send(
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
                key,
                RequireAssignedCrew: false),
            cancellationToken));
    }
}
