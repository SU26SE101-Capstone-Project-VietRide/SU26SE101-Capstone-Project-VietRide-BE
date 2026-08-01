using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Parcel.Api.Filters;
using VietRide.Parcel.Application.Features.Parcels.OperatorStats;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.Api.Controllers;

[ApiController]
[Route("v1/operator/parcel-stats")]
[Authorize(Roles = "OPERATOR_ADMIN")]
public sealed class OperatorParcelStatsController : ControllerBase
{
    private readonly IMediator _mediator;

    public OperatorParcelStatsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<OperatorParcelStatsResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<OperatorParcelStatsResponse>> GetAsync(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] string? groupBy,
        [FromQuery] int? limit,
        CancellationToken cancellationToken)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required.");
        var result = await _mediator.Send(
            new GetOperatorParcelStatsQuery(operatorId, from, to, groupBy, limit),
            cancellationToken);
        return Ok(result);
    }
}
