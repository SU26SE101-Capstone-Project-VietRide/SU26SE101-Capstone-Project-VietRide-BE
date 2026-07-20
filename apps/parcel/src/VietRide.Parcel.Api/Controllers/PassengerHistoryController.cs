using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Parcel.Application.Features.PassengerHistory;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.Api.Controllers;

[ApiController]
[Route("v1/passenger/history")]
[Authorize(Roles = "PASSENGER")]
public sealed class PassengerHistoryController : ControllerBase
{
    private readonly IMediator _mediator;

    public PassengerHistoryController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<PassengerHistoryItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<PagedResult<PassengerHistoryItemDto>>> GetAsync(
        [FromQuery] string? type,
        [FromQuery] string? status,
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new GetPassengerHistoryQuery(
                CurrentUserClaims.GetUserId(User),
                type ?? string.Empty,
                status,
                from,
                to,
                page,
                pageSize),
            cancellationToken);

        return Ok(result);
    }
}
