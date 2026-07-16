using FluentValidation;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.Application.Features.Incidents.ReportIncident;

public sealed class ReportIncidentCommandValidator : AbstractValidator<ReportIncidentCommand>
{
    private static readonly HashSet<string> Categories =
        Enum.GetNames<IncidentCategory>().ToHashSet(StringComparer.Ordinal);

    public ReportIncidentCommandValidator()
    {
        RuleFor(command => command.TripId).NotEmpty();
        RuleFor(command => command.ReporterUserId).NotEmpty();
        RuleFor(command => command.Category)
            .NotEmpty()
            .Must(category => Categories.Contains(category))
            .WithMessage("Category must be one of TRAFFIC_JAM, VEHICLE_BREAKDOWN, ACCIDENT, WEATHER, OTHER.");
        RuleFor(command => command.Description)
            .Must(description => description is null || description.Trim().Length <= 500)
            .WithMessage("Description cannot exceed 500 characters.");
        RuleFor(command => command.PhotoUrls)
            .Must(photoUrls => photoUrls is null || photoUrls.Count <= 3)
            .WithMessage("At most three photo URLs are allowed.");
        RuleForEach(command => command.PhotoUrls)
            .Must(BeAbsoluteHttpsUrl)
            .WithMessage("Each photo URL must be an absolute HTTPS URL.");
        RuleFor(command => command)
            .Must(command => command.Latitude.HasValue == command.Longitude.HasValue)
            .WithMessage("Latitude and longitude must be supplied together.");
        RuleFor(command => command.Latitude)
            .InclusiveBetween(-90m, 90m)
            .When(command => command.Latitude.HasValue);
        RuleFor(command => command.Longitude)
            .InclusiveBetween(-180m, 180m)
            .When(command => command.Longitude.HasValue);
    }

    private static bool BeAbsoluteHttpsUrl(string value)
    {
        var normalized = value?.Trim();
        return Uri.TryCreate(normalized, UriKind.Absolute, out var uri)
            && string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
    }
}
