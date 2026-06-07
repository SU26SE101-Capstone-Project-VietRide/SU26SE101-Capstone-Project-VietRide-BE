using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Identity.Api.Controllers.Requests;
using VietRide.Identity.Application.Features.Operators.RegisterOperator;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Identity.Api.Controllers;

[ApiController]
[Route("v1/operators")]
public sealed class OperatorsController : ControllerBase
{
    private readonly ISender _sender;

    public OperatorsController(ISender sender)
    {
        _sender = sender;
    }

    /// <summary>Public operator self-registration.</summary>
    [HttpPost("register")]
    [AllowAnonymous]
    [ProducesResponseType(typeof(ApiResponse<RegisterOperatorResponseDto>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<RegisterOperatorResponseDto>> Register(
        [FromBody] RegisterOperatorRequest request,
        CancellationToken cancellationToken)
    {
        var response = await _sender.Send(
            new RegisterOperatorCommand(
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
                request.Password),
            cancellationToken);

        return StatusCode(StatusCodes.Status201Created, response);
    }
}
