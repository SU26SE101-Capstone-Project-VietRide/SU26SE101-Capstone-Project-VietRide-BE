using MediatR;

namespace VietRide.Identity.Application.Features.Admin.CreateOperator;

public sealed record CreateOperatorCommand(
    string CallerRole,
    Guid CallerUserId,
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
    IReadOnlyCollection<string> UnsupportedSubscriptionFields) : IRequest<CreateOperatorResponseDto>;
