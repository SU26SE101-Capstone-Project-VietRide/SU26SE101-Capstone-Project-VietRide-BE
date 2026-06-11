using System.Text.Json;
using System.Text.Json.Serialization;

namespace VietRide.Identity.Api.Controllers.Requests;

/// <summary>POST /v1/admin/operators request body.</summary>
public sealed class CreateOperatorRequest
{
    private static readonly HashSet<string> UnsupportedSubscriptionFieldNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "planId",
        "subscriptionStatus",
        "paymentMethod",
        "paidPlan",
        "plan",
        "subscriptionPlan",
    };

    public CreateOperatorRequest()
    {
    }

    public CreateOperatorRequest(
        string name,
        string contactEmail,
        string contactPhone,
        string businessRegistrationNumber,
        string taxCode,
        string addressStreet,
        string addressWard,
        string addressDistrict,
        string addressProvince,
        string representativeName,
        string representativePhone)
    {
        Name = name;
        ContactEmail = contactEmail;
        ContactPhone = contactPhone;
        BusinessRegistrationNumber = businessRegistrationNumber;
        TaxCode = taxCode;
        AddressStreet = addressStreet;
        AddressWard = addressWard;
        AddressDistrict = addressDistrict;
        AddressProvince = addressProvince;
        RepresentativeName = representativeName;
        RepresentativePhone = representativePhone;
    }

    public string Name { get; init; } = string.Empty;
    public string ContactEmail { get; init; } = string.Empty;
    public string ContactPhone { get; init; } = string.Empty;
    public string BusinessRegistrationNumber { get; init; } = string.Empty;
    public string TaxCode { get; init; } = string.Empty;
    public string AddressStreet { get; init; } = string.Empty;
    public string AddressWard { get; init; } = string.Empty;
    public string AddressDistrict { get; init; } = string.Empty;
    public string AddressProvince { get; init; } = string.Empty;
    public string RepresentativeName { get; init; } = string.Empty;
    public string RepresentativePhone { get; init; } = string.Empty;

    [JsonExtensionData]
    public IDictionary<string, JsonElement>? ExtensionData { get; init; }

    public IReadOnlyCollection<string> GetUnsupportedSubscriptionFields()
        => ExtensionData is null
            ? []
            : ExtensionData.Keys
                .Where(UnsupportedSubscriptionFieldNames.Contains)
                .ToArray();
}
