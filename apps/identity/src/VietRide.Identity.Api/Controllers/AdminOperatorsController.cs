using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Identity.Api.Controllers.Requests;
using VietRide.Identity.Application.Features.Admin.CreateOperator;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Identity.Api.Controllers;

[ApiController]
[Route("v1/admin/operators")]
[Authorize(Roles = "SYSTEM_ADMIN")]
public sealed class AdminOperatorsController : ControllerBase
{
    private readonly ISender _sender;

    public AdminOperatorsController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>Creates an approved operator and initial OPERATOR_ADMIN account.</summary>
    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<CreateOperatorResponseDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CreateOperatorResponseDto>> Create(
        [FromBody] CreateOperatorRequest request,
        CancellationToken cancellationToken)
    {
        var callerUserId = CurrentUserClaims.GetUserId(User);
        var callerRole = CurrentUserClaims.GetRole(User);

        var response = await _sender.Send(
            new CreateOperatorCommand(
                callerRole,
                callerUserId,
                request.Name,
                request.ContactEmail,
                request.ContactPhone,
                request.BusinessRegistrationNumber,
                request.TaxCode,
                request.AddressStreet,
                request.AddressWard,
                request.AddressDistrict,
                request.AddressProvince,
                request.RepresentativeName,
                request.RepresentativePhone,
                request.GetUnsupportedSubscriptionFields()),
            cancellationToken);

        return StatusCode(StatusCodes.Status201Created, response);
    }
}
