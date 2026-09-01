using MediatR;
using VietRide.Parcel.Application.Abstractions.ServiceClients;
using VietRide.Parcel.Application.Exceptions;
using VietRide.Parcel.Application.Features.History;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.Application.Features.PassengerHistory;

public sealed class GetPassengerHistoryQueryHandler
    : IRequestHandler<GetPassengerHistoryQuery, PagedResult<PassengerHistoryItemDto>>
{
    private readonly IBookingServiceClient _bookings;
    private readonly SentParcelHistoryReader _parcels;
    private readonly IPaymentRedirectLookupClient _paymentRedirectLookup;
    private readonly IClock _clock;

    public GetPassengerHistoryQueryHandler(
        IBookingServiceClient bookings,
        SentParcelHistoryReader parcels,
        IPaymentRedirectLookupClient paymentRedirectLookup,
        IClock clock)
    {
        _bookings = bookings;
        _parcels = parcels;
        _paymentRedirectLookup = paymentRedirectLookup;
        _clock = clock;
    }

    public async Task<PagedResult<PassengerHistoryItemDto>> Handle(
        GetPassengerHistoryQuery request,
        CancellationToken cancellationToken)
    {
        if (request.Type.Equals("TICKET", StringComparison.OrdinalIgnoreCase))
            return await GetTicketHistoryAsync(request, cancellationToken);

        var parcelPage = await _parcels.ReadForPassengerHistoryAsync(
            request.UserId,
            request.Status,
            request.From,
            request.To,
            request.Page,
            request.PageSize,
            cancellationToken);
        var paymentRedirectUrls = await GetParcelPaymentRedirectUrlsAsync(
            request.UserId,
            parcelPage.Items,
            cancellationToken);
        var parcelItems = parcelPage.Items.Select(parcel => new PassengerHistoryItemDto(
            "PARCEL",
            parcel.History.ParcelId,
            parcel.History.ParcelCode,
            parcel.History.TripId,
            parcel.History.Status,
            parcel.History.CreatedAt,
            parcel.History.TotalAmount,
            parcel.History.OriginName,
            parcel.History.DestinationName,
            parcel.History.DepartureDateTime,
            parcel.History.EstimatedArrivalTime,
            null,
            new ParcelHistoryDetailsDto(
                parcel.History.BookingId,
                parcel.History.RecipientName,
                parcel.History.SizeCategory,
                parcel.History.PhotoUrl,
                parcel.History.DeliveryMethod),
            paymentRedirectUrls.GetValueOrDefault(parcel.History.ParcelId),
            CreateTrackingTarget(parcel.DropoffStopId, parcel.DestinationStationId)))
            .ToList();

        return PagedResult<PassengerHistoryItemDto>.Create(
            parcelItems,
            parcelPage.Page,
            parcelPage.PageSize,
            parcelPage.TotalItems);
    }

    private async Task<PagedResult<PassengerHistoryItemDto>> GetTicketHistoryAsync(
        GetPassengerHistoryQuery request,
        CancellationToken cancellationToken)
    {
        var outcome = await _bookings.GetPassengerHistoryAsync(
            request.UserId,
            request.Status,
            request.From,
            request.To,
            request.Page,
            request.PageSize,
            cancellationToken);
        if (!outcome.IsSuccess || outcome.Page is null)
        {
            throw new PassengerHistoryUpstreamUnavailableException(
                "Booking history is temporarily unavailable.");
        }

        var items = outcome.Page.Items.Select(booking => new PassengerHistoryItemDto(
            "TICKET",
            booking.BookingId,
            booking.BookingCode,
            booking.TripId,
            booking.Status,
            booking.CreatedAt,
            booking.TotalAmount,
            booking.OriginName,
            booking.DestinationName,
            booking.DepartureDateTime,
            null,
            new TicketHistoryDetailsDto(
                booking.BookingGroupId,
                booking.TripDirection,
                booking.RouteName,
                booking.Tickets.Select(ticket => new PassengerHistoryTicketDto(
                    ticket.TicketId,
                    ticket.TicketCode,
                    ticket.SeatNumber,
                    ticket.Status,
                    ticket.PaidAmount)).ToList(),
                booking.Vehicle is null
                    ? null
                    : new PassengerHistoryVehicleDto(
                        booking.Vehicle.LicensePlate,
                        booking.Vehicle.VehicleType is null
                            ? null
                            : new PassengerHistoryVehicleTypeDto(
                                booking.Vehicle.VehicleType.Code,
                                booking.Vehicle.VehicleType.DisplayName)),
                PickupPoint: MapPoint(booking.PickupPoint),
                DropoffPoint: MapPoint(booking.DropoffPoint)),
            null,
            booking.PaymentRedirectUrl,
            CreateTrackingTarget(booking.DropoffStopId, booking.DropoffStationId)))
            .ToList();

        return PagedResult<PassengerHistoryItemDto>.Create(
            items,
            outcome.Page.Page,
            outcome.Page.PageSize,
            outcome.Page.TotalItems);
    }

    private async Task<IReadOnlyDictionary<Guid, string>> GetParcelPaymentRedirectUrlsAsync(
        Guid userId,
        IReadOnlyCollection<PassengerParcelHistoryProjection> parcels,
        CancellationToken cancellationToken)
    {
        var now = _clock.UtcNow;
        var candidates = parcels
            .Select(parcel => CreateRedirectCandidate(parcel, now))
            .Where(candidate => candidate is not null)
            .Select(candidate => candidate!)
            .GroupBy(candidate => (candidate.ReferenceType, candidate.ReferenceId))
            .ToDictionary(group => group.Key, group => group.First());
        if (candidates.Count == 0)
            return new Dictionary<Guid, string>();

        IReadOnlyList<PaymentRedirectLookupItem> lookupItems;
        try
        {
            lookupItems = await _paymentRedirectLookup.LookupAsync(
                userId,
                candidates.Keys
                    .Select(reference => new PaymentRedirectLookupReference(
                        reference.ReferenceType,
                        reference.ReferenceId))
                    .ToArray(),
                cancellationToken);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw;
        }
        catch (Exception)
        {
            return new Dictionary<Guid, string>();
        }

        return lookupItems
            .GroupBy(item => (item.ReferenceType, item.ReferenceId))
            .Where(group => group.Count() == 1)
            .Select(group => group.Single())
            .Where(item => candidates.TryGetValue(
                    (item.ReferenceType, item.ReferenceId),
                    out var candidate)
                && item.PaymentId == candidate.PaymentId
                && item.Amount == candidate.Amount
                && item.DueAt > now
                && item.DueAt <= candidate.Deadline
                && !string.IsNullOrWhiteSpace(item.PaymentRedirectUrl))
            .ToDictionary(
                item => candidates[(item.ReferenceType, item.ReferenceId)].ParcelId,
                item => item.PaymentRedirectUrl);
    }

    private static RedirectCandidate? CreateRedirectCandidate(
        PassengerParcelHistoryProjection parcel,
        DateTimeOffset now)
    {
        if (parcel.Status == ParcelStatus.PENDING_PAYMENT
            && parcel.DepositPaymentId.HasValue
            && parcel.DepositPaymentId.Value != Guid.Empty
            && parcel.DepositRemainingAmount > 0
            && parcel.LatestCheckInAt.HasValue
            && parcel.LatestCheckInAt.Value > now)
        {
            return new RedirectCandidate(
                parcel.History.ParcelId,
                parcel.DepositPaymentId.Value,
                "PARCEL",
                parcel.History.ParcelId,
                parcel.DepositRemainingAmount,
                parcel.LatestCheckInAt.Value);
        }

        if (parcel.Status == ParcelStatus.PENDING_FINAL_PAYMENT
            && parcel.BalancePaymentId.HasValue
            && parcel.BalancePaymentId.Value != Guid.Empty
            && parcel.BalanceRemainingAmount > 0
            && parcel.FinalPaymentDeadline.HasValue
            && parcel.FinalPaymentDeadline.Value > now)
        {
            return new RedirectCandidate(
                parcel.History.ParcelId,
                parcel.BalancePaymentId.Value,
                "PARCEL_ADDITIONAL",
                parcel.History.ParcelId,
                parcel.BalanceRemainingAmount,
                parcel.FinalPaymentDeadline.Value);
        }

        return null;
    }

    private static PassengerTrackingTargetDto? CreateTrackingTarget(
        Guid? stopId,
        Guid? stationId)
        => stopId.HasValue
            ? new PassengerTrackingTargetDto("STOP", StopId: stopId)
            : stationId.HasValue
                ? new PassengerTrackingTargetDto("STATION", StationId: stationId)
                : null;

    private static PassengerHistoryPointDto? MapPoint(BookingHistoryPointDto? point)
        => point is null
            ? null
            : new PassengerHistoryPointDto(
                point.Type,
                point.Id,
                point.DisplayName,
                point.Address,
                point.PlannedAt);

    private sealed record RedirectCandidate(
        Guid ParcelId,
        Guid PaymentId,
        string ReferenceType,
        Guid ReferenceId,
        long Amount,
        DateTimeOffset Deadline);
}
