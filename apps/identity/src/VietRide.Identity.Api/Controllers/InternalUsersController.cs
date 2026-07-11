using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Identity.Api.Controllers.Requests;
using VietRide.Identity.Application.Features.Devices.GetActiveDeviceTokens;
using VietRide.Identity.Application.Features.Devices.RemoveDeviceToken;
using VietRide.Identity.Application.Features.InternalUsers.GetInternalUser;
using VietRide.Shared.Web.Authentication;

namespace VietRide.Identity.Api.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = InternalJwtAuthenticationExtensions.Scheme)]
[Route("internal/v1/users")]
public sealed class InternalUsersController : ControllerBase
{
    private readonly IMediator _mediator;

    public InternalUsersController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IReadOnlyList<GetInternalUserResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<IReadOnlyList<GetInternalUserResponseDto>>> GetUsers(
        [FromQuery] Guid[] ids,
        CancellationToken cancellationToken)
    {
        if (ids.Length is 0 or > 100)
            return UnprocessableEntity();

        return Ok(await _mediator.Send(new GetInternalUsersQuery(ids.Distinct().ToArray()), cancellationToken));
    }

    [HttpGet("{userId:guid}")]
    [ProducesResponseType(typeof(GetInternalUserResponseDto), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<GetInternalUserResponseDto>> GetUser(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetInternalUserQuery(userId), cancellationToken);

        return Ok(result);
    }

    [HttpPost("{userId:guid}/device-tokens/deactivate")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> DeactivateDeviceToken(
        Guid userId,
        [FromBody] RemoveDeviceTokenRequest request,
        CancellationToken cancellationToken)
    {
        await _mediator.Send(new RemoveDeviceTokenCommand(userId, request.FcmToken), cancellationToken);
        return NoContent();
    }
    [HttpGet("{userId:guid}/device-tokens")]
    [ProducesResponseType(typeof(IReadOnlyList<GetActiveDeviceTokensResponseDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<GetActiveDeviceTokensResponseDto>>> GetDeviceTokens(
        Guid userId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetActiveDeviceTokensQuery(userId), cancellationToken);

        return Ok(result);
    }
}
