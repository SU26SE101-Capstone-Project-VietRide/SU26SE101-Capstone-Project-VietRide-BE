namespace VietRide.Identity.Application.Features.Internal.Operators.GetOperatorSummaries;

public sealed record GetOperatorSummariesRequest(IReadOnlyList<Guid> OperatorIds);
