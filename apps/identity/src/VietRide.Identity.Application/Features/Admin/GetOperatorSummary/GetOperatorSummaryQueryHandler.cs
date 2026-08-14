using MediatR;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Identity.Application.Features.Admin.GetOperatorSummary;

public sealed class GetOperatorSummaryQueryHandler(IOperatorRepository operators)
    : IRequestHandler<GetOperatorSummaryQuery, AdminOperatorSummaryDto>
{
    public Task<AdminOperatorSummaryDto> Handle(
        GetOperatorSummaryQuery request,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(request.CallerRole, UserRole.SYSTEM_ADMIN.ToString(), StringComparison.Ordinal))
            throw new ForbiddenException("FORBIDDEN", "Only SYSTEM_ADMIN can view operator summary.");
        return operators.GetSummaryAsync(cancellationToken);
    }
}
