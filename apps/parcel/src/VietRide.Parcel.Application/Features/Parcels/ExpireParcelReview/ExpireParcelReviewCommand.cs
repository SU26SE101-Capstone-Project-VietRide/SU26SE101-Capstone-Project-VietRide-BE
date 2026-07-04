using MediatR;
using VietRide.Shared.Application.Behaviors;

namespace VietRide.Parcel.Application.Features.Parcels.ExpireParcelReview;

[SkipTransaction]
public sealed record ExpireParcelReviewCommand() : IRequest<int>;
