using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Trip.Domain.Entities;

public sealed class RouteChangeProposalStop : IAuditable
{
    public Guid ProposalId { get; private set; }
    public Guid StopId { get; private set; }
    public int OrderIndex { get; private set; }
    public int EstimatedDurationFromOriginMinutes { get; private set; }
    public decimal? DistanceFromOriginKm { get; private set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset UpdatedAt { get; set; }

    private RouteChangeProposalStop() { }

    public static RouteChangeProposalStop Create(
        Guid proposalId,
        Guid stopId,
        int orderIndex,
        int estimatedDurationFromOriginMinutes,
        decimal? distanceFromOriginKm)
    {
        if (proposalId == Guid.Empty) throw new ArgumentException("Value cannot be empty.", nameof(proposalId));
        if (stopId == Guid.Empty) throw new ArgumentException("Value cannot be empty.", nameof(stopId));
        if (orderIndex <= 0) throw new ArgumentOutOfRangeException(nameof(orderIndex));
        if (estimatedDurationFromOriginMinutes < 0) throw new ArgumentOutOfRangeException(nameof(estimatedDurationFromOriginMinutes));
        if (distanceFromOriginKm < 0m) throw new ArgumentOutOfRangeException(nameof(distanceFromOriginKm));
        return new RouteChangeProposalStop
        {
            ProposalId = proposalId,
            StopId = stopId,
            OrderIndex = orderIndex,
            EstimatedDurationFromOriginMinutes = estimatedDurationFromOriginMinutes,
            DistanceFromOriginKm = distanceFromOriginKm,
        };
    }
}
