using MediatR;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Identity.Application.Features.Operators;

public sealed class GetOperatorProfileHandler : IRequestHandler<GetOperatorProfileQuery, OperatorProfileResponse>
{
    private readonly IOperatorRepository operatorRepository;

    public GetOperatorProfileHandler(IOperatorRepository operatorRepository)
    {
        this.operatorRepository = operatorRepository;
    }

    public async Task<OperatorProfileResponse> Handle(GetOperatorProfileQuery request, CancellationToken cancellationToken)
    {
        var operatorProfile = await operatorRepository.GetByIdAsync(request.OperatorId, cancellationToken)
            ?? throw new NotFoundException(nameof(Operator), request.OperatorId);

        return OperatorProfileResponse.FromOperator(operatorProfile);
    }
}
