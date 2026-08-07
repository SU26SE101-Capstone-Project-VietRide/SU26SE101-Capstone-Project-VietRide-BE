using MediatR;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Kernel.Primitives;
using BookingEntity = VietRide.Booking.Domain.Entities.Booking;

namespace VietRide.Booking.Application.Features.Bookings.History;

public sealed class GetBookingHistoryQueryHandler
    : IRequestHandler<GetBookingHistoryQuery, PagedResult<BookingHistoryItemDto>>
{
    private readonly IBookingRepository _bookings;
    private readonly IPaymentRedirectLookupClient _paymentRedirectLookup;

    public GetBookingHistoryQueryHandler(
        IBookingRepository bookings,
        IPaymentRedirectLookupClient paymentRedirectLookup)
    {
        _bookings = bookings;
        _paymentRedirectLookup = paymentRedirectLookup;
    }

    public async Task<PagedResult<BookingHistoryItemDto>> Handle(
        GetBookingHistoryQuery request,
        CancellationToken cancellationToken)
    {
        var range = BookingHistoryDateRange.Parse(request.From, request.To);
        var status = request.Status is null
            ? (BookingStatus?)null
            : Enum.Parse<BookingStatus>(request.Status, true);
        var page = await _bookings.ListPassengerHistoryAsync(
            request.UserId,
            status,
            range.From,
            range.To,
            request.Page,
            request.PageSize,
            cancellationToken);

        var pendingBookings = page.Items
            .Where(booking => booking.Status == BookingStatus.PENDING_PAYMENT)
            .ToList();
        var paymentRedirectUrls = await GetPaymentRedirectUrlsAsync(
            request.UserId,
            pendingBookings,
            cancellationToken);

        var items = page.Items.Select(booking => new BookingHistoryItemDto(
            booking.Id,
            booking.BookingCode.Value,
            booking.TripId,
            booking.Status.ToString(),
            booking.CreatedAt,
            booking.TotalAmount.Amount,
            booking.TripSnapshotOriginName,
            booking.TripSnapshotDestName,
            booking.TripCurrentDeparture ?? booking.TripSnapshotDeparture,
            booking.BookingGroupId,
            booking.TripDirection?.ToString(),
            booking.TripSnapshotRouteName,
            booking.Tickets
                .OrderBy(ticket => ticket.SeatNumber, StringComparer.Ordinal)
                .ThenBy(ticket => ticket.Id)
                .Select(ticket => new BookingHistoryTicketDto(
                    ticket.Id,
                    ticket.TicketCode.Value,
                    ticket.SeatNumber,
                    ticket.Status.ToString(),
                    ticket.PaidAmount.Amount))
                .ToList(),
            paymentRedirectUrls.GetValueOrDefault(booking.Id),
            booking.DropoffStationId,
            booking.DropoffStopId))
            .ToList();

        return PagedResult<BookingHistoryItemDto>.Create(
            items,
            page.Page,
            page.PageSize,
            page.TotalItems);
    }

    private async Task<IReadOnlyDictionary<Guid, string>> GetPaymentRedirectUrlsAsync(
        Guid userId,
        IReadOnlyCollection<BookingEntity> pendingBookings,
        CancellationToken cancellationToken)
    {
        if (pendingBookings.Count == 0)
            return new Dictionary<Guid, string>();

        var groupIds = pendingBookings
            .Where(booking => booking.BookingGroupId.HasValue)
            .Select(booking => booking.BookingGroupId!.Value)
            .Distinct()
            .ToArray();
        var groupTotals = groupIds.Length == 0
            ? new Dictionary<Guid, long>()
            : await _bookings.GetBookingGroupNetTotalsAsync(groupIds, cancellationToken);

        var expectedAmounts = new Dictionary<(string ReferenceType, Guid ReferenceId), long>();
        var bookingReferences = new Dictionary<Guid, (string ReferenceType, Guid ReferenceId)>();
        foreach (var booking in pendingBookings)
        {
            var reference = booking.BookingGroupId.HasValue
                ? (ReferenceType: "BOOKING_GROUP", ReferenceId: booking.BookingGroupId.Value)
                : (ReferenceType: "BOOKING", ReferenceId: booking.Id);
            var amount = booking.BookingGroupId.HasValue
                ? groupTotals.GetValueOrDefault(booking.BookingGroupId.Value, -1)
                : booking.TotalAmount.Amount;

            if (amount < 0)
                continue;

            expectedAmounts[reference] = amount;
            bookingReferences[booking.Id] = reference;
        }

        if (expectedAmounts.Count == 0)
            return new Dictionary<Guid, string>();

        IReadOnlyList<PaymentRedirectLookupItem> lookupItems;
        try
        {
            lookupItems = await _paymentRedirectLookup.LookupAsync(
                userId,
                expectedAmounts.Keys
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

        var eligibleUrls = lookupItems
            .GroupBy(item => (item.ReferenceType, item.ReferenceId))
            .Where(group => group.Count() == 1)
            .Select(group => group.Single())
            .Where(item => expectedAmounts.TryGetValue(
                    (item.ReferenceType, item.ReferenceId),
                    out var expectedAmount)
                && item.Amount == expectedAmount
                && !string.IsNullOrWhiteSpace(item.PaymentRedirectUrl))
            .ToDictionary(
                item => (item.ReferenceType, item.ReferenceId),
                item => item.PaymentRedirectUrl);

        return bookingReferences
            .Where(entry => eligibleUrls.ContainsKey(entry.Value))
            .ToDictionary(entry => entry.Key, entry => eligibleUrls[entry.Value]);
    }
}
