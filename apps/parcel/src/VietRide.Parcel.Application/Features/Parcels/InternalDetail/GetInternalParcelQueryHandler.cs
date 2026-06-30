using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Parcel.Application.Features.Parcels.InternalDetail;

public sealed class GetInternalParcelQueryHandler
    : IRequestHandler<GetInternalParcelQuery, InternalParcelResponse>
{
    private readonly IParcelRepository _parcelRepository;

    public GetInternalParcelQueryHandler(IParcelRepository parcelRepository)
    {
        _parcelRepository = parcelRepository;
    }

    public async Task<InternalParcelResponse> Handle(
        GetInternalParcelQuery query,
        CancellationToken cancellationToken)
    {
        var parcel = await _parcelRepository.GetByIdAsync(query.ParcelId, cancellationToken);
        if (parcel is null)
            throw new CodedNotFoundException(
                "PARCEL_NOT_FOUND",
                $"Parcel with id '{query.ParcelId}' not found.");

        return new InternalParcelResponse(
            parcel.Id,
            parcel.TripId,
            parcel.Status.ToString(),
            parcel.SenderUserId,
            parcel.RecipientUserId,
            parcel.OperatorId,
            parcel.DropoffStopId);
    }
}
