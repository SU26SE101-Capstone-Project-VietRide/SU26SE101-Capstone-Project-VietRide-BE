using MediatR;

namespace VietRide.Parcel.Application.Features.ParcelRouteFares.Summary;

public sealed record GetParcelRouteFareSummaryQuery(Guid OperatorId)
    : IRequest<IReadOnlyList<ParcelRouteFareSummaryItem>>;

public sealed record ParcelRouteFareSummaryItem(
    Guid RouteId,
    IReadOnlyList<string> ConfiguredSizeCategories,
    bool HasActiveWindow,
    bool HasScheduledWindow);
