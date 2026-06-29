using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Parcel.Api.Controllers.Requests;
using VietRide.Parcel.Api.Filters;
using VietRide.Parcel.Application.Features.Parcels.Reweigh;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.Api.Controllers;

[ApiController]
[Route("v1/assistant/parcels")]
[Authorize(Roles = "ASSISTANT")]
public sealed class AssistantParcelsController : ControllerBase
{
    private readonly IMediator _mediator;

    public AssistantParcelsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPost("{parcelId:guid}/reweigh")]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(ApiResponse<ReweighParcelResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ReweighParcelResponse>> ReweighAsync(
        Guid parcelId,
        [FromBody] ReweighParcelRequest request,
        CancellationToken cancellationToken)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required.");

        var result = await _mediator.Send(new ReweighParcelCommand(
            parcelId,
            operatorId,
            request.ActualWeightKg,
            request.ActualSizeCategory,
            request.PaymentMethod), cancellationToken);

        return Ok(result);
    }
}
