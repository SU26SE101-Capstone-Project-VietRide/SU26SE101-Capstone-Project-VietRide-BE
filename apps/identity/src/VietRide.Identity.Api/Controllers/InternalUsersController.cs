using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Identity.Application.Features.Devices.GetActiveDeviceTokens;
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
