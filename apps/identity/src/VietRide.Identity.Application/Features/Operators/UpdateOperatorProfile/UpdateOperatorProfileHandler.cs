using MediatR;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Identity.Application.Features.Operators;

public sealed class UpdateOperatorProfileHandler : IRequestHandler<UpdateOperatorProfileCommand, OperatorProfileResponse>
{
    private const string OperatorAdminRole = "OPERATOR_ADMIN";

    private readonly IOperatorRepository operatorRepository;

    public UpdateOperatorProfileHandler(IOperatorRepository operatorRepository)
    {
        this.operatorRepository = operatorRepository;
    }

    public async Task<OperatorProfileResponse> Handle(UpdateOperatorProfileCommand request, CancellationToken cancellationToken)
    {
        if (!string.Equals(request.CallerRole, OperatorAdminRole, StringComparison.Ordinal))
        {
            throw new ForbiddenException("FORBIDDEN", "Only OPERATOR_ADMIN can update operator profile.");
        }

        var operatorProfile = await operatorRepository.GetByIdAsync(request.OperatorId, cancellationToken)
            ?? throw new NotFoundException(nameof(Operator), request.OperatorId);

        if (operatorProfile.RegistrationStatus != OperatorRegistrationStatus.APPROVED)
        {
            throw new ForbiddenException("FORBIDDEN", "Operator must be approved to update profile.");
        }

        var cancellationPolicy = OperatorProfilePolicyValidator.NormalizeCancellationPolicy(request.CancellationPolicy);
        var parcelNoShowPolicy = OperatorProfilePolicyValidator.NormalizeParcelNoShowPolicy(request.ParcelNoShowPolicy);
        var luggagePolicy = OperatorProfilePolicyValidator.NormalizeLuggagePolicy(request.LuggagePolicy);

        operatorProfile.UpdateProfile(
            request.Name,
            operatorProfile.ContactEmail,
            PhoneNumber.Normalize(request.ContactPhone).ToString(),
            request.LogoUrl,
            request.AddressStreet,
            request.AddressWard,
            request.AddressDistrict,
            request.AddressProvince,
            request.RepresentativeName,
            PhoneNumber.Normalize(request.RepresentativePhone).ToString(),
            cancellationPolicy?.GetRawText(),
            parcelNoShowPolicy.GetRawText(),
            luggagePolicy.GetRawText());

        operatorRepository.Update(operatorProfile);

        return OperatorProfileResponse.FromOperator(operatorProfile);
    }
}
