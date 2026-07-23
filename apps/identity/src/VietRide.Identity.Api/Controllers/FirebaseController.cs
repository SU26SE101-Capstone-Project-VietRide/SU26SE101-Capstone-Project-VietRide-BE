using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;
using VietRide.Identity.Api.Controllers.Requests;
using VietRide.Identity.Application.Features.Firebase.CreateFirebaseCustomToken;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Web.Idempotency;

namespace VietRide.Identity.Api.Controllers;

[ApiController]
[Route("v1/firebase")]
[Authorize]
public sealed class FirebaseController : ControllerBase
{
    private readonly ISender _sender;

    public FirebaseController(ISender sender)
    {
        _sender = sender;
    }

    [HttpPost("custom-token")]
    [SkipIdempotency("Firebase custom-token responses contain credentials and must not be cached in Redis.")]
    [ProducesResponseType(typeof(ApiResponse<FirebaseCustomTokenResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status502BadGateway)]
    public async Task<ActionResult<FirebaseCustomTokenResponse>> CreateCustomTokenAsync(
        [FromBody(EmptyBodyBehavior = EmptyBodyBehavior.Allow)]
        CreateFirebaseCustomTokenRequest? request,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new CreateFirebaseCustomTokenCommand(
                CurrentUserClaims.GetUserId(User),
                CurrentUserClaims.GetRole(User),
                CurrentUserClaims.GetOperatorId(User),
                request?.Purpose),
            cancellationToken);

        return Ok(result);
    }
}
