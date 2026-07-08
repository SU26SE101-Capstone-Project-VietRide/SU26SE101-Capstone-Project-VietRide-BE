using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Parcel.Application.Features.Vouchers;

public sealed class GetParcelAvailableVouchersQueryHandler
    : IRequestHandler<GetParcelAvailableVouchersQuery, IReadOnlyList<AvailableVoucherDto>>
{
    private readonly ITripServiceClient _tripClient;
    private readonly IParcelRouteFareRepository _fareRepository;
    private readonly IBookingServiceClient _bookingClient;

    public GetParcelAvailableVouchersQueryHandler(
        ITripServiceClient tripClient,
        IParcelRouteFareRepository fareRepository,
        IBookingServiceClient bookingClient)
    {
        _tripClient = tripClient;
        _fareRepository = fareRepository;
        _bookingClient = bookingClient;
    }

    public async Task<IReadOnlyList<AvailableVoucherDto>> Handle(
        GetParcelAvailableVouchersQuery request,
        CancellationToken cancellationToken)
    {
        if (!Enum.TryParse<ParcelSizeCategory>(request.SizeCategory, true, out var sizeCategory))
            throw new CodedValidationException("INVALID_SIZE_CATEGORY", "Invalid parcel size category.");

        if (sizeCategory == ParcelSizeCategory.EXTRA_LARGE)
            return [];

        var tripOutcome = await _tripClient.GetTripParcelSnapshotAsync(request.TripId, cancellationToken);
        if (tripOutcome.Kind == TripSnapshotOutcomeKind.TripNotFound)
            throw new CodedNotFoundException("TRIP_NOT_FOUND", "Trip not found.");
        if (tripOutcome.Kind == TripSnapshotOutcomeKind.TransportError)
            throw new ParcelDependencyUnavailableException("TRIP_SERVICE_UNAVAILABLE", tripOutcome.ErrorMessage ?? "Trip service unavailable.");

        var trip = tripOutcome.Snapshot!;
        var amount = request.OrderAmount;
        if (!amount.HasValue)
        {
            var fare = await _fareRepository.FindByCompositeAsync(trip.RouteId, sizeCategory, cancellationToken);
            amount = fare?.PriceVnd.Amount ?? 0;
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
