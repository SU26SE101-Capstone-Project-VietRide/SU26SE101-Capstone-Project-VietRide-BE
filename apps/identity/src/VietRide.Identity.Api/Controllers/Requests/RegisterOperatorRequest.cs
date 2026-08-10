namespace VietRide.Identity.Api.Controllers.Requests;

/// <summary>POST /v1/operators/register request body.</summary>
public sealed record RegisterOperatorRequest(
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
    string Password);
