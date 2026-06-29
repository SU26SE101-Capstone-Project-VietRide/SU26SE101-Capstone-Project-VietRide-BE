using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Parcel.Api.Controllers.Requests;
using VietRide.Parcel.Api.Filters;
using VietRide.Parcel.Application.Features.ParcelRouteFares.Create;
using VietRide.Parcel.Application.Features.ParcelRouteFares.List;
using VietRide.Parcel.Application.Features.ParcelRouteFares.Update;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.Api.Controllers;

[ApiController]
[Route("v1/operator/parcel-route-fares")]
public sealed class OperatorParcelRouteFaresController : ControllerBase
{
    private const string AdminRole = "OPERATOR_ADMIN";
    private const string StaffRole = "OPERATOR_STAFF";

    private readonly IMediator _mediator;

    public OperatorParcelRouteFaresController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost]
    [Authorize(Roles = AdminRole)]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(ApiResponse<ParcelRouteFareResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ParcelRouteFareResponse>> CreateAsync(
        [FromBody] CreateParcelRouteFareRequest request,
        CancellationToken cancellationToken)
    {
        var operatorId = GetRequiredOperatorId();

        var result = await _mediator.Send(new CreateParcelRouteFareCommand(
            operatorId,
            request.RouteId,
            request.SizeCategory,
            request.PriceVnd,
            request.EffectiveFrom,
            request.EffectiveUntil), cancellationToken);

        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpGet]
    [Authorize(Roles = $"{AdminRole},{StaffRole}")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<ParcelRouteFareResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<ParcelRouteFareResponse>>> ListAsync(
        [FromQuery] Guid? routeId,
        [FromQuery] string? sizeCategory,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var operatorId = GetRequiredOperatorId();

        var result = await _mediator.Send(
            new ListParcelRouteFaresQuery(operatorId, routeId, sizeCategory, page, pageSize),
            cancellationToken);

        return Ok(result);
    }

    [HttpPatch("{routeId:guid}/{sizeCategory}")]
    [Authorize(Roles = AdminRole)]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(ApiResponse<ParcelRouteFareResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ParcelRouteFareResponse>> UpdateAsync(
        Guid routeId,
        string sizeCategory,
        [FromBody] UpdateParcelRouteFareRequest request,
        CancellationToken cancellationToken)
    {
        var operatorId = GetRequiredOperatorId();

        var result = await _mediator.Send(new UpdateParcelRouteFareCommand(
            operatorId,
            routeId,
            sizeCategory,
            request.PriceVnd,
            request.EffectiveFrom,
            request.EffectiveUntil), cancellationToken);

        return Ok(result);
    }

    private Guid GetRequiredOperatorId()
        => CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required.");
}
