using MediatR;

namespace VietRide.Identity.Application.Features.Operators.RegisterOperator;

public sealed record RegisterOperatorCommand(
    string Name,
    string ContactEmail,
    string ContactPhone,
    string BusinessRegistrationNumber,
    string TaxCode,
    string AddressStreet,
    string AddressWard,
    string AddressProvince,
    string RepresentativeName,
    string RepresentativePhone,
    string Password) : IRequest<RegisterOperatorResponseDto>;
