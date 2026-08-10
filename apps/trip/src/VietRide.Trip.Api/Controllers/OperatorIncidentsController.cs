using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Application.Features.Incidents.OperatorIncidents;

namespace VietRide.Trip.Api.Controllers;

[ApiController]
[Route("v1/operator/incidents")]
[Authorize(Roles = "OPERATOR_ADMIN,OPERATOR_STAFF")]
public sealed class OperatorIncidentsController : ControllerBase
{
    private readonly IMediator mediator;

    public OperatorIncidentsController(IMediator mediator)
    {
        this.mediator = mediator;
    }

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<OperatorIncidentDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PagedResult<OperatorIncidentDto>>> ListAsync(
        [FromQuery] Guid? tripId,
        [FromQuery] string? category,
        [FromQuery] string? status,
        [FromQuery] DateOnly? from,
        [FromQuery] DateOnly? to,
        [FromQuery] int? page,
        [FromQuery] int? pageSize,
        CancellationToken cancellationToken)
        => Ok(await mediator.Send(
            new ListOperatorIncidentsQuery(
                GetRequiredOperatorId(),
                tripId,
                category,
                status,
                from,
                to,
                page,
                pageSize),
            cancellationToken));

    [HttpGet("{incidentId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<OperatorIncidentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OperatorIncidentDto>> GetAsync(
        Guid incidentId,
        CancellationToken cancellationToken)
        => Ok(await mediator.Send(
            new GetOperatorIncidentQuery(GetRequiredOperatorId(), incidentId),
            cancellationToken));

    [HttpGet("{incidentId}")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public ActionResult RejectMalformedIncidentId(string incidentId)
        => throw new CodedValidationException(
            "VALIDATION_ERROR",
            "incidentId must be a valid non-empty UUID.",
            [new ValidationError("incidentId", "incidentId must be a valid non-empty UUID.")]);

    private Guid GetRequiredOperatorId()
        => CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required to view incidents.");
}
