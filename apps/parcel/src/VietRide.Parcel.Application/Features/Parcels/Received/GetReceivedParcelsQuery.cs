using VietRide.Shared.Application.Cqrs;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Parcel.Application.Features.Parcels.Received;

public sealed record GetReceivedParcelsQuery(Guid UserId, int Page, int PageSize) : IQuery<PagedResult<ReceivedParcelResponse>>;
