using System.Security.Claims;
using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Payment.Application.Abstractions.Services;
using VietRide.Payment.Application.Features.Invoices;
using VietRide.Payment.Application.Features.Management;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Payment.Api.Controllers;

[ApiController]
[Route("v1/operator/invoices")]
[Authorize(Roles = "OPERATOR_ADMIN")]
public sealed class OperatorInvoicesController : ControllerBase
{
    private readonly ISender _sender;

    public OperatorInvoicesController(ISender sender) => _sender = sender;

    [HttpGet]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<InvoiceListItemDto>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<PagedResult<InvoiceListItemDto>>> List(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        [FromQuery] string? status = null,
        [FromQuery] DateTimeOffset? from = null,
        [FromQuery] DateTimeOffset? to = null,
        [FromQuery] string? sortBy = null,
        [FromQuery] string sortDir = "desc",
        CancellationToken cancellationToken = default)
        => Ok(await _sender.Send(new ListOperatorInvoicesQuery(
            GetOperatorId(), new PageOptions(page, pageSize, sortBy, sortDir, from, to), status), cancellationToken));

    [HttpGet("{invoiceId:guid}")]
    [ProducesResponseType(typeof(ApiResponse<InvoiceDetailDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<InvoiceDetailDto>> Detail(Guid invoiceId, CancellationToken cancellationToken)
        => Ok(await _sender.Send(new GetOperatorInvoiceQuery(GetOperatorId(), invoiceId), cancellationToken));

    /// <summary>Generate a fresh, short-lived signed URL for an issued invoice PDF.</summary>
    [HttpGet("{invoiceId:guid}/download")]
    [ProducesResponseType(typeof(ApiResponse<InvoiceDownloadUrl>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status429TooManyRequests)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status500InternalServerError)]
    public async Task<ActionResult<InvoiceDownloadUrl>> Download(
        Guid invoiceId,
        CancellationToken cancellationToken)
    {
        var result = await _sender.Send(
            new DownloadInvoiceQuery(invoiceId, GetOperatorId(), GetUserId()),
            cancellationToken);
        return Ok(result);
    }

    private Guid GetOperatorId()
    {
        var value = User.FindFirstValue("operatorId") ?? User.FindFirstValue("operator_id");
        if (!Guid.TryParse(value, out var operatorId))
            throw new UnauthorizedAccessException("Authenticated caller operatorId claim is missing or invalid.");
        return operatorId;
    }

    private Guid GetUserId()
    {
        var value = User.FindFirstValue("sub")
            ?? User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (!Guid.TryParse(value, out var userId))
            throw new UnauthorizedAccessException("Authenticated caller sub claim is missing or invalid.");
        return userId;
    }
}
