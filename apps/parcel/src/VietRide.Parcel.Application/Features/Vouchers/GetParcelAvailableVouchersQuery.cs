using MediatR;
using VietRide.Parcel.Application.Abstractions.ServiceClients;

namespace VietRide.Parcel.Application.Features.Vouchers;

public sealed record GetParcelAvailableVouchersQuery(
    Guid UserId,
    Guid TripId,
    string SizeCategory,
    string? PaymentMethod,
    long? OrderAmount) : IRequest<IReadOnlyList<AvailableVoucherDto>>;
