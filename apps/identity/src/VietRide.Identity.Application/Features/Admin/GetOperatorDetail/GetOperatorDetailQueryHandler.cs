using MediatR;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Identity.Application.Features.Admin.GetOperatorDetail;

public sealed class GetOperatorDetailQueryHandler : IRequestHandler<GetOperatorDetailQuery, AdminOperatorDetailDto>
{
    private readonly IOperatorRepository _operators;

    public GetOperatorDetailQueryHandler(IOperatorRepository operators)
    {
        _operators = operators;
    }

    public async Task<AdminOperatorDetailDto> Handle(
        GetOperatorDetailQuery request,
        CancellationToken cancellationToken)
    {
        if (!string.Equals(request.CallerRole, UserRole.SYSTEM_ADMIN.ToString(), StringComparison.Ordinal))
            throw new ForbiddenException("FORBIDDEN", "Only SYSTEM_ADMIN can read operator details.");

        var operatorEntity = await _operators.GetByIdNoTrackingAsync(request.OperatorId, cancellationToken)
            ?? throw new NotFoundException(nameof(Operator), request.OperatorId);

        return AdminOperatorDetailDto.FromOperator(operatorEntity);
    }
}
