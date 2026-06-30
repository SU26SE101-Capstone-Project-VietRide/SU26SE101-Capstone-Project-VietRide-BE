using MediatR;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Parcel.Api.Controllers.Requests;
using VietRide.Parcel.Api.Filters;
using VietRide.Parcel.Application.Features.Parcels.AvailableTrips;
using VietRide.Parcel.Application.Features.Parcels.Create;
using VietRide.Parcel.Application.Features.Parcels.Detail;
using VietRide.Parcel.Application.Features.Parcels.Received;
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
        [FromQuery] decimal estimatedWeightKg,
        [FromQuery] string sizeCategory,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20,
        CancellationToken cancellationToken = default)
    {
        var result = await _mediator.Send(
            new AvailableTripsQuery(
                originStationId,
                destinationStationId,
                departureDate,
                estimatedWeightKg,
                sizeCategory,
                page,
                pageSize),
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
                null,
                request.BookingId,
                request.ItemName,
                request.Description,
                request.PhotoUrl,
                request.SizeCategory,
                request.EstimatedWeightKg,
                request.DeliveryMethod,
                request.PaymentMethod),
            cancellationToken);

        return StatusCode(StatusCodes.Status201Created, result);
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
}
