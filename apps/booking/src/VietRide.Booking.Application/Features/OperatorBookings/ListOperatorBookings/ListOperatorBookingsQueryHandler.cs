using MediatR;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Booking.Application.Features.OperatorBookings.ListOperatorBookings;

public sealed class ListOperatorBookingsQueryHandler
    : IRequestHandler<ListOperatorBookingsQuery, PagedResult<OperatorBookingListItem>>
{
    private static readonly HashSet<string> AllowedSortFields =
        ["createdAt", "departureAt", "bookingCode", "status", "totalAmount"];
    private static readonly TimeZoneInfo IctTimeZone = ResolveIctTimeZone();
    private readonly IBookingRepository _bookings;
    private readonly IIdentityUserServiceClient _identityUsers;

    public ListOperatorBookingsQueryHandler(
        IBookingRepository bookings,
        IIdentityUserServiceClient identityUsers)
    {
        _bookings = bookings;
        _identityUsers = identityUsers;
    }

    public async Task<PagedResult<OperatorBookingListItem>> Handle(
        ListOperatorBookingsQuery request,
        CancellationToken cancellationToken)
    {
        if (request.SortBy is not null && !AllowedSortFields.Contains(request.SortBy))
            throw new BadRequestException("INVALID_SORT_FIELD", $"Unsupported sort field '{request.SortBy}'.");

        var effectivePageSize = Math.Min(request.PageSize, 100);
        Guid? passengerUserId = null;
        if (request.PassengerPhone is not null)
        {
            var canonicalPhone = PhoneNumber.Normalize(request.PassengerPhone).Value;
            passengerUserId = await _identityUsers.GetUserIdByPhoneAsync(canonicalPhone, cancellationToken);
            if (passengerUserId is null)
                return PagedResult<OperatorBookingListItem>.Create([], request.Page, effectivePageSize, 0);
        }

        var statuses = request.Status?.Split(',', StringSplitOptions.TrimEntries)
            .Select(value => Enum.Parse<BookingStatus>(value, true))
            .ToArray();
        var (from, to) = ToUtcInterval(request.Date);
        var criteria = new OperatorBookingListCriteria(
            request.OperatorId,
            statuses,
            request.TripId,
            from,
            to,
            passengerUserId,
            request.BookingCode?.Trim(),
            request.Page,
            effectivePageSize,
            request.SortBy ?? "createdAt",
            request.SortDir.Equals("desc", StringComparison.OrdinalIgnoreCase));
        var result = await _bookings.ListOperatorBookingsAsync(criteria, cancellationToken);
        var items = result.Items;
        var missingBuyerIds = items
            .Where(item => item.Buyer is null && item.BuyerUserId != Guid.Empty)
            .Select(item => item.BuyerUserId)
            .Distinct()
            .ToArray();
        if (missingBuyerIds.Length > 0)
        {
            var profiles = await _identityUsers.GetUsersAsync(missingBuyerIds, cancellationToken);
            items = items
                .Select(item => item.Buyer is not null
                    ? item
                    : item with { Buyer = ToBuyer(item.BuyerUserId, profiles) })
                .ToArray();
        }

        return PagedResult<OperatorBookingListItem>.Create(
            items, request.Page, effectivePageSize, result.TotalItems);
    }

    private static OperatorBookingBuyerDto? ToBuyer(
        Guid buyerUserId,
        IReadOnlyDictionary<Guid, BookingBuyerSnapshotProfile> profiles)
    {
        var profile = profiles.TryGetValue(buyerUserId, out var resolved)
            ? resolved
            : new BookingBuyerSnapshotProfile(
                buyerUserId,
                BookingBuyerSnapshotProfile.DeletedDisplayName,
                null,
                null,
                null,
                true);
        return new OperatorBookingBuyerDto(
            profile.UserId,
            profile.DisplayName,
            profile.Phone,
            profile.Email,
            profile.AvatarUrl);
    }

    private static (DateTimeOffset? From, DateTimeOffset? To) ToUtcInterval(DateOnly? date)
    {
        if (date is null)
            return (null, null);

        var localFrom = date.Value.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        var localTo = date.Value.AddDays(1).ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return (
            new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localFrom, IctTimeZone), TimeSpan.Zero),
            new DateTimeOffset(TimeZoneInfo.ConvertTimeToUtc(localTo, IctTimeZone), TimeSpan.Zero));
    }

    private static TimeZoneInfo ResolveIctTimeZone()
    {
        try
        {
            return TimeZoneInfo.FindSystemTimeZoneById("Asia/Ho_Chi_Minh");
        }
        catch (TimeZoneNotFoundException)
        {
            try
            {
                return TimeZoneInfo.FindSystemTimeZoneById("SE Asia Standard Time");
            }
            catch (TimeZoneNotFoundException)
            {
                return TimeZoneInfo.CreateCustomTimeZone(
                    "Asia/Ho_Chi_Minh", TimeSpan.FromHours(7), "ICT", "ICT");
            }
        }
    }
}
