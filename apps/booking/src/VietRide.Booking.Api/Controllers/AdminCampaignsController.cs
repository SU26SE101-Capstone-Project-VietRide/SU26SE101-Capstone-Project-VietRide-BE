using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Booking.Api.Controllers.Requests;
using VietRide.Booking.Application.Features.Campaigns;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Booking.Api.Controllers;

[ApiController]
[Route("v1/admin/campaigns")]
[Authorize(Roles = "SYSTEM_ADMIN")]
public sealed class AdminCampaignsController : ControllerBase
{
    private const string IdempotencyKeyHeader = "Idempotency-Key";

    private readonly ISender _sender;

    public AdminCampaignsController(ISender sender)
    {
        _sender = sender;
    }

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<CampaignDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> List(CancellationToken ct)
    {
        var result = await _sender.Send(new ListCampaignsQuery(), ct);
        return Ok(result);
    }

    [HttpPost]
    [ProducesResponseType(typeof(ApiResponse<CampaignDto>), StatusCodes.Status201Created)]
    public async Task<IActionResult> Create([FromBody] CampaignRequest request, CancellationToken ct)
    {
        GetRequiredIdempotencyKey();
        var result = await _sender.Send(
            new CreateCampaignCommand(
                request.Name,
                request.Description,
                request.OwnerOperatorId,
                request.ValidFrom,
                request.ValidUntil,
                request.VoucherIds,
                GetUserId()),
            ct);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPatch("{campaignId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<CampaignDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Patch(Guid campaignId, [FromBody] CampaignRequest request, CancellationToken ct)
    {
        GetRequiredIdempotencyKey();
        var result = await _sender.Send(
            new UpdateCampaignCommand(
                campaignId,
                request.Name,
                request.Description,
                request.ValidFrom,
                request.ValidUntil,
                request.IsActive,
                request.VoucherIds),
            ct);
        return Ok(result);
    }

    [HttpPost("{campaignId:guid}/activate")]
    [ProducesResponseType(typeof(ApiResponse<CampaignDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Activate(Guid campaignId, CancellationToken ct)
    {
        GetRequiredIdempotencyKey();
        var result = await _sender.Send(new SetCampaignActiveCommand(campaignId, true), ct);
        return Ok(result);
    }

    [HttpPost("{campaignId:guid}/deactivate")]
    [ProducesResponseType(typeof(ApiResponse<CampaignDto>), StatusCodes.Status200OK)]
    public async Task<IActionResult> Deactivate(Guid campaignId, CancellationToken ct)
    {
        GetRequiredIdempotencyKey();
        var result = await _sender.Send(new SetCampaignActiveCommand(campaignId, false), ct);
        return Ok(result);
    }

    private Guid GetUserId()
    {
        var sub = User.FindFirstValue("sub") ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(sub, out var userId))
            throw new UnauthorizedAccessException("Authenticated caller sub claim is missing or invalid.");
        return userId;
    }

    private string GetRequiredIdempotencyKey()
    {
        var value = Request.Headers.TryGetValue(IdempotencyKeyHeader, out var values)
            ? values.ToString()
            : string.Empty;

        if (string.IsNullOrWhiteSpace(value))
        {
            throw new CodedValidationException(
                "VALIDATION_ERROR",
                "Idempotency-Key header is required.");
        }

        return value;
    }
}
