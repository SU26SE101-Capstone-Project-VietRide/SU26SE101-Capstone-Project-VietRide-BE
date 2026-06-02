using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Identity.Application.Features.Auth.GetJwks;

namespace VietRide.Identity.Api.Controllers;

/// <summary>
/// JWKS discovery endpoint for RS256 public key distribution.
/// GET /v1/.well-known/jwks.json (public, no auth required).
/// Per VietRide_API_Contract_v1.md lines 118–136.
/// </summary>
[ApiController]
[Route("v1/.well-known")]
[AllowAnonymous]
public sealed class JwksController : ControllerBase
{
    private readonly ISender _sender;

    public JwksController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>Returns the RS256 public key as a JWKS document.</summary>
    /// <remarks>
    /// The <c>kid</c> in this response matches the <c>kid</c> header in access tokens.
    /// </remarks>
    [HttpGet("jwks.json")]
    [ProducesResponseType(typeof(object), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetJwks(CancellationToken ct)
    {
        var json = await _sender.Send(new GetJwksQuery(), ct);

        return Content(json, "application/json");
    }
}
