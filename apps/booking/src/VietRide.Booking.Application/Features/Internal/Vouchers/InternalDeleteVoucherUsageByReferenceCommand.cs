using MediatR;

namespace VietRide.Booking.Application.Features.Internal.Vouchers;

public sealed record InternalDeleteVoucherUsageByReferenceCommand(string ReferenceType, Guid ReferenceId) : IRequest<Unit>;
