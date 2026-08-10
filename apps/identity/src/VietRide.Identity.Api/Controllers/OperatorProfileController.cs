using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Identity.Api.Controllers.Requests;
using VietRide.Identity.Application.Features.Operators;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Identity.Api.Controllers;

[ApiController]
[Route("v1/operator/profile")]
public sealed class OperatorProfileController : ControllerBase
{
    private const string OperatorAdminRole = "OPERATOR_ADMIN";
    private const string OperatorStaffRole = "OPERATOR_STAFF";

    private readonly IMediator mediator;

    public OperatorProfileController(IMediator mediator)
    {
        this.mediator = mediator;
    }

    [HttpGet]
    [Authorize(Roles = OperatorAdminRole + "," + OperatorStaffRole)]
    [ProducesResponseType(typeof(ApiResponse<OperatorProfileResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OperatorProfileResponse>> GetAsync(CancellationToken cancellationToken)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required to read operator profile.");

        return Ok(await mediator.Send(new GetOperatorProfileQuery(operatorId), cancellationToken));
    }

    [HttpPatch]
    [Authorize(Roles = OperatorAdminRole)]
    [ProducesResponseType(typeof(ApiResponse<OperatorProfileResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<OperatorProfileResponse>> PatchAsync(
        [FromBody] UpdateOperatorProfileRequest request,
        CancellationToken cancellationToken)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required to update operator profile.");
        var callerRole = CurrentUserClaims.GetRole(User);

        var command = new UpdateOperatorProfileCommand(
            operatorId,
            callerRole,
            request.Name,
            request.ContactPhone,
            request.LogoUrl,
            request.AddressStreet,
            request.AddressWard,
            request.AddressProvince,
            request.RepresentativeName,
            request.RepresentativePhone,
            request.CancellationPolicy,
            request.ParcelNoShowPolicy,
            request.LuggagePolicy);

        return Ok(await mediator.Send(command, cancellationToken));
    }
}
