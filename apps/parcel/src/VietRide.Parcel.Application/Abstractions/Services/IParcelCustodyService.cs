using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.Application.Abstractions.Services;

public interface IParcelCustodyService
{
    Task<ParcelCustodyEvent> AppendAsync(
        ParcelEntity parcel,
        ParcelCustodyEventType eventType,
        ParcelCustodyLocationType? actualLocationType,
        Guid? actualLocationId,
        string? locationSnapshot,
        Guid? actorId,
        string actorRole,
        string source,
        string? idempotencyKey,
        IReadOnlyCollection<string>? evidenceReferences,
        string? reason,
        CancellationToken cancellationToken = default);
}
