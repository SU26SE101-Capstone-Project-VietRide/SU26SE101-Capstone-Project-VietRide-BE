using System.Text.Json;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Api.Controllers.Requests;
using VietRide.Trip.Api.Filters;
using VietRide.Trip.Application.Features.Stations;

namespace VietRide.Trip.Api.Controllers;

[ApiController]
[Route("v1/operator/stations")]
public sealed class OperatorStationsController : ControllerBase
{
    private const string OperatorRoles = "OPERATOR_STAFF,OPERATOR_ADMIN";
    private const string OperatorWriteRole = "OPERATOR_ADMIN";

    private readonly IMediator mediator;

    public OperatorStationsController(IMediator mediator)
    {
        this.mediator = mediator;
    }

    [HttpGet]
    [Authorize(Roles = OperatorRoles)]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<OperatorStationDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<OperatorStationDto>>> GetAsync([FromQuery] int? page, [FromQuery] int? pageSize, [FromQuery] string? search, CancellationToken cancellationToken)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required to manage operator stations.");
        return Ok(await mediator.Send(new ListOperatorStationsQuery(operatorId, page, pageSize, search), cancellationToken));
    }

    [HttpPost]
    [Authorize(Roles = OperatorRoles)]
    [ProducesResponseType(typeof(ApiResponse<CreateOrLinkOperatorStationResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse<CreateOrLinkOperatorStationResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CreateOrLinkOperatorStationResponse>> PostAsync(
        [FromBody] CreateOrLinkOperatorStationRequest request,
        CancellationToken cancellationToken)
    {
        var operatorId = CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required to manage operator stations.");

        var isLinkBranch = request.StationId.HasValue;
        var command = new CreateOrLinkOperatorStationCommand(
            operatorId,
            request.StationId,
            request.Name,
            request.City,
            request.Province,
            request.Latitude,
            request.Longitude,
            request.AddressStreet,
            isLinkBranch ? null : request.ContactPhone,
            request.ContactEmail,
            Serialize(request.OperatingHours),
            Serialize(request.Facilities),
            request.SupportsShuttle,
            request.DisplayNameOverride,
            request.CounterLocation,
            isLinkBranch ? request.ContactPhone : null,
            request.Instructions,
            request.LocationId,
            request.LocationCode);

        var response = await mediator.Send(command, cancellationToken);
        if (response.Warning is not null)
        {
            return Ok(response);
        }

        return request.StationId.HasValue ? Ok(response) : StatusCode(StatusCodes.Status201Created, response);
    }

    [HttpPatch("{stationId:guid}")]
    [RequireIdempotencyKey]
    [Authorize(Roles = OperatorWriteRole)]
    [ProducesResponseType(typeof(ApiResponse<OperatorStationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OperatorStationDto>> PatchAsync(
        Guid stationId,
        [FromBody] UpdateOperatorStationRequest request,
        CancellationToken cancellationToken)
    {
        return Ok(await mediator.Send(new UpdateOperatorStationCommand(
            GetRequiredOperatorId(), stationId, request.DisplayNameOverride, request.CounterLocation,
            request.ContactPhone, request.Instructions), cancellationToken));
    }

    [HttpDelete("{stationId:guid}")]
    [RequireIdempotencyKey]
    [Authorize(Roles = OperatorWriteRole)]
    [ProducesResponseType(typeof(ApiResponse<OperatorStationDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OperatorStationDto>> DeleteAsync(Guid stationId, CancellationToken cancellationToken)
    {
        return Ok(await mediator.Send(new DeactivateOperatorStationCommand(GetRequiredOperatorId(), stationId), cancellationToken));
    }

    private Guid GetRequiredOperatorId()
        => CurrentUserClaims.GetOperatorId(User)
            ?? throw new ForbiddenException("FORBIDDEN", "Operator scope is required to manage operator stations.");

    private static string? Serialize(JsonElement? value)
        => value is null || value.Value.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            ? null
            : value.Value.GetRawText();
}
