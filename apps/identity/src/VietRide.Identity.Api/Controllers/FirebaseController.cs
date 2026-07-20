using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Identity.Application.Features.Firebase.CreateFirebaseCustomToken;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Identity.Api.Controllers;

[ApiController]
[Route("v1/firebase")]
[Authorize(Roles = "OPERATOR_ADMIN")]
public sealed class FirebaseController : ControllerBase
{
    private readonly ISender _sender;

    public FirebaseController(ISender sender)
    {
        _sender = sender;
    }

    [HttpPost("custom-token")]
    [ProducesResponseType(typeof(ApiResponse<FirebaseCustomTokenResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<FirebaseCustomTokenResponse>> CreateCustomTokenAsync(
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new CreateFirebaseCustomTokenCommand(
                CurrentUserClaims.GetUserId(User),
                CurrentUserClaims.GetRole(User),
                CurrentUserClaims.GetOperatorId(User)),
            cancellationToken);

        return Ok(result);
    }
}
