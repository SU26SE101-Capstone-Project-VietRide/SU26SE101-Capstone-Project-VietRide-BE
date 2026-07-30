using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Web.Authentication;
using VietRide.Shared.Web.Idempotency;
using VietRide.Trip.Api.Controllers.Requests;
using VietRide.Trip.Application.Features.Internal.OperatorAnalytics;

namespace VietRide.Trip.Api.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = InternalJwtAuthenticationExtensions.Scheme)]
[Route("internal/v1/operators")]
public sealed class InternalOperatorsController : ControllerBase
{
    private readonly IMediator mediator;

    public InternalOperatorsController(IMediator mediator)
    {
        this.mediator = mediator;
    }

    [HttpPost("vehicle-counts/batch")]
    [SkipIdempotency("Operator vehicle counting is a read-only query exposed as POST for a bounded request payload.")]
    [ProducesResponseType(typeof(IReadOnlyList<OperatorVehicleCountResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<IReadOnlyList<OperatorVehicleCountResponse>>> GetVehicleCountsAsync(
        [FromBody] OperatorVehicleCountsBatchRequest request,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new GetOperatorVehicleCountsQuery(request.OperatorIds ?? []),
            cancellationToken);
        return Ok(result);
    }

    [HttpGet("{operatorId:guid}/route-performance")]
    [ProducesResponseType(typeof(IReadOnlyList<OperatorRoutePerformanceResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<IReadOnlyList<OperatorRoutePerformanceResponse>>> GetRoutePerformanceAsync(
        Guid operatorId,
        [FromQuery] string? month,
        CancellationToken cancellationToken)
    {
        var result = await mediator.Send(
            new GetOperatorRoutePerformanceQuery(operatorId, month),
            cancellationToken);
        return Ok(result);
    }
}
