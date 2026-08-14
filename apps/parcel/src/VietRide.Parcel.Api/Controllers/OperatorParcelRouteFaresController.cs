using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Parcel.Api.Controllers.Requests;
using VietRide.Parcel.Api.Filters;
using VietRide.Parcel.Application.Features.ParcelRouteFares.Batch;
using VietRide.Parcel.Application.Features.ParcelRouteFares.Create;
using VietRide.Parcel.Application.Features.ParcelRouteFares.List;
using VietRide.Parcel.Application.Features.ParcelRouteFares.Summary;
using VietRide.Parcel.Application.Features.ParcelRouteFares.Update;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Web.Filters;

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
    [AllowedQueryParameters("routeId", "sizeCategory", "page", "pageSize", "search", "sortBy", "sortDir", "effectiveAt", "status")]
    [Authorize(Roles = $"{AdminRole},{StaffRole}")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<ParcelRouteFareResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<PagedResult<ParcelRouteFareResponse>>> ListAsync(
        [FromQuery] Guid? routeId,
        [FromQuery] string? sizeCategory,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? search = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string? sortDir = null,
        [FromQuery] DateOnly? effectiveAt = null,
        [FromQuery] string? status = null,
        CancellationToken cancellationToken = default)
    {
        var operatorId = GetRequiredOperatorId();

        var result = await _mediator.Send(
            new ListParcelRouteFaresQuery(
                operatorId, routeId, sizeCategory, page, pageSize, search,
                sortBy, sortDir, effectiveAt, status),
            cancellationToken);

        return Ok(result);
    }

    [HttpGet("summary")]
    [AllowedQueryParameters]
    [Authorize(Roles = $"{AdminRole},{StaffRole}")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ParcelRouteFareSummaryItem>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ParcelRouteFareSummaryItem>>> SummaryAsync(
        CancellationToken cancellationToken)
        => Ok(await _mediator.Send(
            new GetParcelRouteFareSummaryQuery(GetRequiredOperatorId()), cancellationToken));

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

    [HttpPut("{routeId:guid}/batch")]
    [Authorize(Roles = AdminRole)]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(ApiResponse<BatchParcelRouteFareResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<BatchParcelRouteFareResponse>> BatchAsync(
        Guid routeId,
        [FromBody] BatchParcelRouteFareRequest request,
        CancellationToken cancellationToken)
    {
        var operatorId = GetRequiredOperatorId();
        var items = request.Items?.Select(item => new BatchParcelRouteFareItem(
                item?.SizeCategory,
                item?.PriceVnd ?? 0))
            .ToArray() ?? [];

        var result = await _mediator.Send(new BatchParcelRouteFareCommand(
            operatorId,
            routeId,
            request.EffectiveFrom,
            request.EffectiveUntil,
            items), cancellationToken);

        return Ok(result);
    }

    private Guid GetRequiredOperatorId()
        => CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required.");
}
