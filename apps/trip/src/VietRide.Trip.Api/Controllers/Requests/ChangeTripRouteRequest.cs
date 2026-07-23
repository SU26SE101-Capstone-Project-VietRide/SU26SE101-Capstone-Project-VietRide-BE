using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace VietRide.Trip.Api.Controllers.Requests;

[JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
public sealed class ChangeTripRouteRequest : IValidatableObject
{
    public required Guid AlternativeRouteId { get; init; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (AlternativeRouteId == Guid.Empty)
        {
            yield return new ValidationResult(
                "Alternative route id must not be empty.",
                [nameof(AlternativeRouteId)]);
        }
    }
}
