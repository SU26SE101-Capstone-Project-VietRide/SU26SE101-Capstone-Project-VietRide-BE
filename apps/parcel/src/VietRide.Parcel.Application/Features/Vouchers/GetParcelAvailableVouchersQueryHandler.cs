using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.Security;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Application.Features.Parcels.Quotes;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Parcel.Application.Features.Vouchers;

public sealed class GetParcelAvailableVouchersQueryHandler
    : IRequestHandler<GetParcelAvailableVouchersQuery, IReadOnlyList<AvailableVoucherDto>>
{
    private readonly ITripServiceClient _tripClient;
    private readonly IBookingServiceClient _bookingClient;
    private readonly ParcelQuoteService _quoteService;
    private readonly IClock _clock;

    public GetParcelAvailableVouchersQueryHandler(
        ITripServiceClient tripClient,
        IParcelRouteFareRepository fareRepository,
        IBookingServiceClient bookingClient,
        IParcelPricingPolicyRepository? policyRepository = null,
        IParcelQuoteTokenService? quoteTokenService = null,
        IClock? clock = null)
    {
        _tripClient = tripClient;
        _bookingClient = bookingClient;
        _quoteService = new ParcelQuoteService(fareRepository, policyRepository, quoteTokenService);
        _clock = clock ?? new SystemClock();
    }

    public async Task<IReadOnlyList<AvailableVoucherDto>> Handle(
        GetParcelAvailableVouchersQuery request,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<ParcelSizeCategory>(request.SizeCategory, true, out var sizeCategory))
            throw new CodedValidationException("INVALID_SIZE_CATEGORY", "Invalid parcel size category.");

        var tripOutcome = await _tripClient.GetTripParcelSnapshotAsync(request.TripId, cancellationToken);
        if (tripOutcome.Kind == TripSnapshotOutcomeKind.TripNotFound)
            throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip not found.");
        if (tripOutcome.Kind == TripSnapshotOutcomeKind.TransportError)
            throw new ParcelDependencyUnavailableException("TRIP_SERVICE_UNAVAILABLE", tripOutcome.ErrorMessage ?? "Trip service unavailable.");

        var trip = tripOutcome.Snapshot!;
        long? amount;
        if (!string.IsNullOrWhiteSpace(request.QuoteToken))
        {
            var payload = await _quoteService.ValidateTokenAsync(
                request.QuoteToken,
                new ParcelQuoteTokenExpectation(
                    request.UserId,
                    request.TripId,
                    trip.RouteId,
                    trip.OperatorId,
                    trip.OriginStation.Id,
                    trip.DestinationStation.Id),
                _clock.UtcNow,
                cancellationToken);
            if (!string.Equals(payload.SizeCategory, sizeCategory.ToString(), StringComparison.Ordinal)
                || (request.OrderAmount.HasValue
                    && request.OrderAmount.Value != payload.EstimatedGrossPriceVnd))
            {
                throw new CodedConflictException(
                    "PARCEL_QUOTE_MISMATCH",
                    "Voucher request does not match the server quote.");
            }

            amount = payload.EstimatedGrossPriceVnd;
        }
        else
        {
            amount = request.OrderAmount;
            if (!amount.HasValue)
            {
                var fare = await _quoteService.FindActiveFareAsync(
                    trip.RouteId,
                    sizeCategory,
                    _clock.UtcNow,
                    cancellationToken);
                amount = fare?.PriceVnd.Amount ?? 0;
            }
        }

        return await _bookingClient.GetAvailableParcelVouchersAsync(
            request.UserId,
            trip.OperatorId,
            trip.RouteId,
            request.PaymentMethod,
            amount,
            cancellationToken);
    }
}
