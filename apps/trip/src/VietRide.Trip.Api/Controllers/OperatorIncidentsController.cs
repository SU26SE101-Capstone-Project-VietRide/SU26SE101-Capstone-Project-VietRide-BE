using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Web.Filters;
using VietRide.Trip.Api.Controllers.Requests;
using VietRide.Trip.Api.Filters;
using VietRide.Trip.Application.Features.Incidents.OperatorIncidents;
using VietRide.Trip.Application.Features.Incidents.ResolveIncident;

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
    [AllowedQueryParameters("tripId", "category", "status", "from", "to", "page", "pageSize", "search", "reportedByUserId", "sortBy", "sortDir")]
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
        [FromQuery] string? search,
        [FromQuery] Guid? reportedByUserId,
        [FromQuery] string? sortBy,
        [FromQuery] string? sortDir,
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
                pageSize,
                search,
                reportedByUserId,
                sortBy,
                sortDir),
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

    [HttpPatch("{incidentId:guid}/resolve")]
    [Authorize(Roles = "OPERATOR_ADMIN")]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(ApiResponse<OperatorIncidentDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<OperatorIncidentDto>> ResolveAsync(
        Guid incidentId,
        [FromBody] ResolveIncidentRequest request,
        CancellationToken cancellationToken)
        => Ok(await mediator.Send(
            new ResolveIncidentCommand(
                GetRequiredOperatorId(),
                CurrentUserClaims.GetUserId(User),
                incidentId,
                request.ResolutionNote),
            cancellationToken));

    [HttpPatch("{incidentId}/resolve")]
    [Authorize(Roles = "OPERATOR_ADMIN")]
    [ApiExplorerSettings(IgnoreApi = true)]
    public ActionResult RejectMalformedResolveIncidentId(string incidentId)
        => throw new CodedValidationException(
            "VALIDATION_ERROR",
            "incidentId must be a valid non-empty UUID.",
            [new ValidationError("incidentId", "incidentId must be a valid non-empty UUID.")]);

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
