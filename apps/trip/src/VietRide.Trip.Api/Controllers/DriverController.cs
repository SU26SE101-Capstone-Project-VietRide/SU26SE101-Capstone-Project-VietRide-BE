using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Application.Features.DriverSchedules.GetMyDriverSchedule;

namespace VietRide.Trip.Api.Controllers;

[ApiController]
[Route("v1/driver")]
[Authorize(Roles = "DRIVER,ASSISTANT")]
public sealed class DriverController : ControllerBase
{
    private readonly IMediator mediator;

    public DriverController(IMediator mediator)
    {
        this.mediator = mediator;
    }

    [HttpGet("me/schedule")]
    [ProducesResponseType(typeof(ApiResponse<GetMyDriverScheduleResult>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<GetMyDriverScheduleResult>> GetMyScheduleAsync(
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserClaims.GetUserId(User);
        return Ok(await mediator.Send(
            new GetMyDriverScheduleQuery(userId, from, to),
            cancellationToken));
    }
}
