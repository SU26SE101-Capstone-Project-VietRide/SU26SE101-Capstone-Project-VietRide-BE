using VietRide.Parcel.Domain.Entities;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.Application.Features.Parcels.OperatorDetail;

public sealed record OperatorParcelDetailData(
    ParcelEntity Parcel,
    IReadOnlyList<ParcelStatusHistory> StatusHistory);
