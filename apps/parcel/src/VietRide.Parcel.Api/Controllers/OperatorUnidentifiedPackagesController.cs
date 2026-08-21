using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Parcel.Application.Features.Reliability.UnidentifiedPackages;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.Api.Controllers;

[ApiController]
[Route("v1/operator/unidentified-packages")]
[Authorize(Roles = "OPERATOR_ADMIN,OPERATOR_STAFF")]
public sealed class OperatorUnidentifiedPackagesController : ControllerBase
{
    private readonly IMediator _mediator;

    public OperatorUnidentifiedPackagesController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<UnidentifiedPackageResponse>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<UnidentifiedPackageResponse>>> ListAsync(
        [FromQuery] string? status,
        [FromQuery] string? search,
        [FromQuery] Guid? tripId,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required.");
        return Ok(await _mediator.Send(
            new ListUnidentifiedPackagesQuery(operatorId, status, search, tripId, page, pageSize),
            cancellationToken));
    }

    [HttpGet("{packageId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<UnidentifiedPackageResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UnidentifiedPackageResponse>> GetDetailAsync(
        Guid packageId,
        CancellationToken cancellationToken)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required.");
        return Ok(await _mediator.Send(
            new GetUnidentifiedPackageQuery(packageId, operatorId),
            cancellationToken));
    }

    [HttpGet("{packageId:guid}/match-candidates")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<UnidentifiedPackageMatchCandidateResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<UnidentifiedPackageMatchCandidateResponse>>> GetCandidatesAsync(
        Guid packageId,
        [FromQuery] int limit = 20,
        CancellationToken cancellationToken = default)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required.");
        return Ok(await _mediator.Send(
            new ListUnidentifiedMatchCandidatesQuery(packageId, operatorId, limit),
            cancellationToken));
    }
}
