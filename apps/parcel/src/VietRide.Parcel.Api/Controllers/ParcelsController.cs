using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Parcel.Api.Controllers.Requests;
using VietRide.Parcel.Api.Filters;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Features.History;
using VietRide.Parcel.Application.Features.Parcels.AvailableTrips;
using VietRide.Parcel.Application.Features.Parcels.Create;
using VietRide.Parcel.Application.Features.Parcels.DepositPayment;
using VietRide.Parcel.Application.Features.Parcels.Detail;
using VietRide.Parcel.Application.Features.Parcels.FinalPayment;
using VietRide.Parcel.Application.Features.Parcels.Received;
using VietRide.Parcel.Application.Features.Parcels.Sent;
using VietRide.Parcel.Application.Features.Reliability.Claims;
using VietRide.Parcel.Application.Features.Reliability.ReportIncident;
using VietRide.Parcel.Application.Features.Reliability.Trace;
using VietRide.Parcel.Application.Features.Vouchers;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.Api.Controllers;

[ApiController]
[Route("v1/parcels")]
public sealed class ParcelsController : ControllerBase
{
    private readonly IMediator _mediator;

    public ParcelsController(IMediator mediator)
    {
        _mediator = mediator;
    }

    [HttpGet("available-trips")]
    [Authorize(Roles = "PASSENGER")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<AvailableTripResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<PagedResult<AvailableTripResponse>>> GetAvailableTripsAsync(
        [FromQuery] Guid originStationId,
        [FromQuery] Guid destinationStationId,
        [FromQuery] DateOnly departureDate,
        [FromQuery] decimal lengthCm,
        [FromQuery] decimal widthCm,
        [FromQuery] decimal heightCm,
        [FromQuery] decimal estimatedWeightKg,
        [FromQuery] string? sizeCategory = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new AvailableTripsQuery(
                originStationId,
                destinationStationId,
                departureDate,
                lengthCm,
                widthCm,
                heightCm,
                estimatedWeightKg,
                sizeCategory,
                page,
                pageSize,
                CurrentUserClaims.GetUserId(User)),
            cancellationToken);

        return Ok(result);
    }

    [HttpPost]
    [Authorize(Roles = "PASSENGER")]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(ApiResponse<CreateParcelResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<CreateParcelResponse>> CreateAsync(
        [FromBody] CreateParcelRequest request,
        CancellationToken cancellationToken)
    {
        var senderUserId = CurrentUserClaims.GetUserId(User);

        var result = await _mediator.Send(
            new CreateParcelCommand(
                senderUserId,
                null,
                request.Recipient.FullName,
                request.Recipient.PhoneNumber,
                request.Recipient.Email,
                request.TripId,
                request.DropoffStopId,
                request.BookingId,
                request.ItemName,
                request.Description,
                request.PhotoUrl,
                request.SizeCategory,
                request.LengthCm,
                request.WidthCm,
                request.HeightCm,
                request.EstimatedWeightKg,
                request.DeliveryMethod,
                request.PaymentMethod,
                request.VoucherCode,
                request.QuoteToken,
                Request.Headers[RequireIdempotencyKeyAttribute.HeaderName].ToString(),
                request.DeclaredValueVnd,
                request.Quantity),
            cancellationToken);

        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPost("{parcelId:guid}/deposit-payment")]
    [Authorize(Roles = "PASSENGER")]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(ApiResponse<ParcelDepositPaymentResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status426UpgradeRequired)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ParcelDepositPaymentResponse>> StartDepositPaymentAsync(
        Guid parcelId,
        [FromBody] StartParcelPaymentRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new StartParcelDepositPaymentCommand(
                parcelId,
                CurrentUserClaims.GetUserId(User),
                request.PaymentMethod,
                Request.Headers[RequireIdempotencyKeyAttribute.HeaderName].ToString(),
                request.PaymentReturnMode),
            cancellationToken);
        return Ok(result);
    }

    [HttpPost("{parcelId:guid}/final-payment")]
    [Authorize(Roles = "PASSENGER")]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(ApiResponse<ParcelFinalPaymentResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status426UpgradeRequired)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<ParcelFinalPaymentResponse>> StartFinalPaymentAsync(
        Guid parcelId,
        [FromBody] StartParcelPaymentRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new StartParcelFinalPaymentCommand(
                parcelId,
                CurrentUserClaims.GetUserId(User),
                request.PaymentMethod,
                Request.Headers[RequireIdempotencyKeyAttribute.HeaderName].ToString(),
                request.PaymentReturnMode),
            cancellationToken);
        return Ok(result);
    }

    [HttpGet("vouchers/available")]
    [Authorize(Roles = "PASSENGER")]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<AvailableVoucherDto>>), StatusCodes.Status200OK)]
    public async Task<IActionResult> GetAvailableVouchersAsync(
        [FromQuery] Guid tripId,
        [FromQuery] string sizeCategory,
        [FromQuery] string? paymentMethod,
        [FromQuery] long? orderAmount,
        [FromQuery] string? quoteToken,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserClaims.GetUserId(User);
        var result = await _mediator.Send(
            new GetParcelAvailableVouchersQuery(userId, tripId, sizeCategory, paymentMethod, orderAmount, quoteToken),
            cancellationToken);
        return Ok(result);
    }

    [HttpGet("received")]
    [Authorize(Roles = "PASSENGER")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<ReceivedParcelResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<PagedResult<ReceivedParcelResponse>>> GetReceivedAsync(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var userId = CurrentUserClaims.GetUserId(User);
        var result = await _mediator.Send(
            new GetReceivedParcelsQuery(userId, page, pageSize),
            cancellationToken);

        return Ok(result);
    }

    [HttpGet("sent")]
    [Authorize(Roles = "PASSENGER")]
    [ProducesResponseType(typeof(ApiResponse<PagedResult<SentParcelHistoryItemDto>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<PagedResult<SentParcelHistoryItemDto>>> GetSentAsync(
        [FromQuery] string? status,
        [FromQuery] string? from,
        [FromQuery] string? to,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new GetSentParcelsQuery(
                CurrentUserClaims.GetUserId(User),
                status,
                from,
                to,
                page,
                pageSize),
            cancellationToken);

        return Ok(result);
    }

    [HttpGet("{parcelId:guid}")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<ParcelDetailResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ParcelDetailResponse>> GetDetailAsync(
        Guid parcelId,
        CancellationToken cancellationToken)
    {
        var userId = CurrentUserClaims.GetUserId(User);
        var operatorId = CurrentUserClaims.GetOperatorId(User);
        var result = await _mediator.Send(
            new GetParcelDetailQuery(parcelId, userId, operatorId),
            cancellationToken);

        return Ok(result);
    }

    [HttpGet("{parcelId:guid}/trace")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<ParcelTraceResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ParcelTraceResponse>> GetTraceAsync(
        Guid parcelId,
        [FromQuery] string? cursor,
        [FromQuery] int limit = 50,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new GetParcelTraceQuery(
                parcelId,
                CurrentUserClaims.GetUserId(User),
                CurrentUserClaims.GetOperatorId(User),
                cursor,
                limit),
            cancellationToken);

        return Ok(result);
    }

    [HttpPost("{parcelId:guid}/incidents")]
    [Authorize]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(ApiResponse<ReportParcelIncidentResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ReportParcelIncidentResponse>> ReportIncidentAsync(
        Guid parcelId,
        [FromBody] ReportParcelIncidentRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new ReportParcelIncidentCommand(
                parcelId,
                CurrentUserClaims.GetUserId(User),
                CurrentUserClaims.GetOperatorId(User),
                request.IncidentType,
                request.Description,
                request.EvidenceUrls),
            cancellationToken);

        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpGet("{parcelId:guid}/incidents")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ParcelIncidentResponse>>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<ParcelIncidentResponse>>> GetIncidentsAsync(
        Guid parcelId,
        CancellationToken cancellationToken)
    {
        var trace = await _mediator.Send(
            new GetParcelTraceQuery(
                parcelId,
                CurrentUserClaims.GetUserId(User),
                CurrentUserClaims.GetOperatorId(User)),
            cancellationToken);
        return Ok(trace.Incidents);
    }

    [HttpGet("{parcelId:guid}/claims")]
    [Authorize]
    [ProducesResponseType(typeof(ApiResponse<IReadOnlyList<ParcelClaimResponse>>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IReadOnlyList<ParcelClaimResponse>>> GetClaimsAsync(
        Guid parcelId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new GetParcelClaimsQuery(
                parcelId,
                CurrentUserClaims.GetUserId(User),
                CurrentUserClaims.GetOperatorId(User)),
            cancellationToken);
        return Ok(result);
    }

    [HttpPost("{parcelId:guid}/claims")]
    [Authorize(Roles = "PASSENGER")]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(ApiResponse<ParcelClaimResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ParcelClaimResponse>> SubmitClaimAsync(
        Guid parcelId,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new SubmitParcelClaimCommand(
                parcelId,
                CurrentUserClaims.GetUserId(User),
                Request.Headers[RequireIdempotencyKeyAttribute.HeaderName].ToString()),
            cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPost("{parcelId:guid}/claims/{claimId:guid}/evidence")]
    [Authorize(Roles = "PASSENGER")]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(ApiResponse<ParcelClaimResponse>), StatusCodes.Status201Created)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ParcelClaimResponse>> AddClaimEvidenceAsync(
        Guid parcelId,
        Guid claimId,
        [FromBody] AddParcelClaimEvidenceRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new AddParcelClaimEvidenceCommand(
                parcelId,
                claimId,
                CurrentUserClaims.GetUserId(User),
                request.EvidenceType,
                request.Reference,
                request.Note),
            cancellationToken);
        return StatusCode(StatusCodes.Status201Created, result);
    }

    [HttpPost("{parcelId:guid}/claims/{claimId:guid}/appeal")]
    [Authorize(Roles = "PASSENGER")]
    [RequireIdempotencyKey]
    [ProducesResponseType(typeof(ApiResponse<ParcelClaimResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ApiResponse), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ParcelClaimResponse>> AppealClaimAsync(
        Guid parcelId,
        Guid claimId,
        [FromBody] AppealParcelClaimRequest request,
        CancellationToken cancellationToken)
    {
        var result = await _mediator.Send(
            new AppealParcelClaimCommand(
                parcelId,
                claimId,
                CurrentUserClaims.GetUserId(User),
                request.Reason,
                Guid.Parse(Request.Headers[RequireIdempotencyKeyAttribute.HeaderName].ToString())),
            cancellationToken);
        return Ok(result);
    }
}
