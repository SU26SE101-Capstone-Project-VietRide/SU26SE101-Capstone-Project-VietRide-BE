using System.Text.Json;
using MediatR;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Identity.Application.Features.Internal.Operators.GetInternalOperator;

public sealed class GetInternalOperatorQueryHandler : IRequestHandler<GetInternalOperatorQuery, InternalOperatorLookupDto>
{
    private readonly IOperatorRepository _operators;

    public GetInternalOperatorQueryHandler(IOperatorRepository operators)
    {
        _operators = operators;
    }

    public async Task<InternalOperatorLookupDto> Handle(
        GetInternalOperatorQuery request,
        CancellationToken cancellationToken)
    {
        var operatorEntity = await _operators.GetByIdNoTrackingAsync(request.OperatorId, cancellationToken)
            ?? throw new NotFoundException(nameof(Operator), request.OperatorId);

        return new InternalOperatorLookupDto(
            operatorEntity.Id,
            operatorEntity.Name,
            operatorEntity.RegistrationStatus.ToString(),
            operatorEntity.IsActive,
            operatorEntity.ContactEmail,
            operatorEntity.ContactPhone,
            operatorEntity.BusinessRegistrationNumber,
            operatorEntity.TaxCode,
            ParseParcelNoShowPolicy(operatorEntity.ParcelNoShowPolicy));
    }

    private static InternalParcelNoShowPolicyDto? ParseParcelNoShowPolicy(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
            return null;

        try
        {
            return JsonSerializer.Deserialize<InternalParcelNoShowPolicyDto>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
