using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Identity.Api.Controllers.Requests;
using VietRide.Identity.Application.Features.Internal.Operators.GetInternalOperator;
using VietRide.Identity.Application.Features.Internal.Operators.GetInternalOperatorSubscription;
using VietRide.Identity.Application.Features.Internal.Operators.GetOperatorRecipientUsers;
using VietRide.Identity.Application.Features.Internal.Operators.GetOperatorCrewUserIds;
using VietRide.Identity.Application.Features.Internal.Operators.IncrementOperatorUsage;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Web.Authentication;

namespace VietRide.Identity.Api.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = InternalJwtAuthenticationExtensions.Scheme)]
[Route("internal/v1/operators")]
public sealed class InternalOperatorsController : ControllerBase
{
    private readonly IMediator _mediator;

    public InternalOperatorsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("{operatorId:guid}")]
    [ProducesResponseType(typeof(InternalOperatorLookupDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<InternalOperatorLookupDto>> GetOperatorAsync(
        Guid operatorId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetInternalOperatorQuery(operatorId), cancellationToken);

        return Ok(result);
    }

    [HttpGet("{operatorId:guid}/subscription")]
    [ProducesResponseType(typeof(InternalOperatorSubscriptionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<InternalOperatorSubscriptionDto>> GetSubscriptionAsync(
        Guid operatorId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetInternalOperatorSubscriptionQuery(operatorId), cancellationToken);

        return Ok(result);
    }

    [HttpGet("{operatorId:guid}/recipient-users")]
    [ProducesResponseType(typeof(IReadOnlyList<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<Guid>>> GetRecipientUsersAsync(
        Guid operatorId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetOperatorRecipientUsersQuery(operatorId), cancellationToken);

        return Ok(result);
    }

    [HttpGet("{operatorId:guid}/crew-user-ids")]
    [ProducesResponseType(typeof(IReadOnlyList<Guid>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<Guid>>> GetCrewUserIdsAsync(
        Guid operatorId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(new GetOperatorCrewUserIdsQuery(operatorId), cancellationToken);
        return Ok(result);
    }
    [HttpPost("{operatorId:guid}/usage/increment")]
    [ProducesResponseType(typeof(InternalOperatorSubscriptionDto), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status402PaymentRequired)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<InternalOperatorSubscriptionDto>> IncrementUsageAsync(
        Guid operatorId,
        [FromBody] IncrementOperatorUsageRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new IncrementOperatorUsageCommand(operatorId, request.Resource, request.Delta),
            cancellationToken);

        return Ok(result);
    }
}
