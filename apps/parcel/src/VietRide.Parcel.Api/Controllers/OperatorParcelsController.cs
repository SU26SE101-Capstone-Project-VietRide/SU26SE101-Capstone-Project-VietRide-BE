using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Parcel.Api.Controllers.Requests;
using VietRide.Parcel.Api.Filters;
using VietRide.Parcel.Application.Features.Parcels.Review;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.Api.Controllers;

[ApiController]
[Route("v1/operator/parcels")]
[Authorize(Roles = "OPERATOR_ADMIN,OPERATOR_STAFF")]
public sealed class OperatorParcelsController : ControllerBase
{
    private readonly IMediator _mediator;

    public OperatorParcelsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpPatch("{parcelId:guid}/review")]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(ApiResponse<ReviewParcelResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ReviewParcelResponse>> ReviewAsync(
        Guid parcelId,
        [FromBody] ReviewParcelRequest request,
        CancellationToken cancellationToken)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required.");
        var userId = CurrentUserClaims.GetUserId(User);

        var result = await _mediator.Send(new ReviewParcelCommand(
            parcelId,
            operatorId,
            userId,
            request.Decision,
            request.DepositAmount,
            request.Reason,
            request.PaymentMethod), cancellationToken);

        return Ok(result);
    }
}
