using MediatR;
using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Parcel.Application.Features.Parcels.AccessCheck;

public sealed class GetParcelAccessCheckQueryHandler
    : IRequestHandler<GetParcelAccessCheckQuery, ParcelAccessCheckResponse>
{
    private readonly IParcelRepository _parcelRepository;

    public GetParcelAccessCheckQueryHandler(IParcelRepository parcelRepository)
    {
        _parcelRepository = parcelRepository;
    }

    public async Task<ParcelAccessCheckResponse> Handle(
        GetParcelAccessCheckQuery query,
        CancellationToken cancellationToken)
    {
        var parcel = await _parcelRepository.GetByIdAsync(query.ParcelId, cancellationToken);
        if (parcel is null)
            throw new CodedNotFoundException(
                "PARCEL_NOT_FOUND",
                $"Parcel with id '{query.ParcelId}' not found.");

        if (query.UserId.HasValue && query.UserId.Value == parcel.SenderUserId)
            return new ParcelAccessCheckResponse(parcel.Id, true, "SENDER");

        if (query.UserId.HasValue && query.UserId.Value == parcel.RecipientUserId)
            return new ParcelAccessCheckResponse(parcel.Id, true, "RECIPIENT");

        if (query.OperatorId.HasValue && query.OperatorId.Value == parcel.OperatorId)
            return new ParcelAccessCheckResponse(parcel.Id, true, "OPERATOR");

        return new ParcelAccessCheckResponse(parcel.Id, false, "NONE");
    }
}
