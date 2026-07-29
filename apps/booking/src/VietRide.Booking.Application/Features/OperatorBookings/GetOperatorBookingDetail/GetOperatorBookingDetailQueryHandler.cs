using MediatR;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.ServiceClients;
using VietRide.Booking.Application.Features.OperatorBookings.ListOperatorBookings;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Booking.Application.Features.OperatorBookings.GetOperatorBookingDetail;

public sealed class GetOperatorBookingDetailQueryHandler : IRequestHandler<GetOperatorBookingDetailQuery, OperatorBookingDetailDto>
{
    private readonly IBookingRepository _bookings;
    private readonly IIdentityUserServiceClient _identityUsers;

    public GetOperatorBookingDetailQueryHandler(
        IBookingRepository bookings,
        IIdentityUserServiceClient identityUsers)
    {
        _bookings = bookings;
        _identityUsers = identityUsers;
    }

    public async Task<OperatorBookingDetailDto> Handle(GetOperatorBookingDetailQuery request, CancellationToken cancellationToken)
    {
        var detail = await _bookings.GetOperatorBookingDetailAsync(request.BookingId, request.OperatorId, cancellationToken);
        if (detail is not null)
        {
            if (detail.Buyer is not null)
            {
                return detail;
            }

            var profiles = await _identityUsers.GetUsersAsync([detail.BuyerUserId], cancellationToken);
            var profile = profiles.TryGetValue(detail.BuyerUserId, out var resolved)
                ? resolved
                : new BookingBuyerSnapshotProfile(
                    detail.BuyerUserId,
                    BookingBuyerSnapshotProfile.DeletedDisplayName,
                    null,
                    null,
                    null,
                    true);
            return detail with
            {
                Buyer = new OperatorBookingBuyerDto(
                    profile.UserId,
                    profile.DisplayName,
                    profile.Phone,
                    profile.Email,
                    profile.AvatarUrl),
            };
        }

        if (await _bookings.BookingExistsAsync(request.BookingId, cancellationToken))
            throw new ForbiddenException("FORBIDDEN", "Booking belongs to another operator.");

        throw new CodedNotFoundException("BOOKING_NOT_FOUND", "Booking not found.");
    }
}
