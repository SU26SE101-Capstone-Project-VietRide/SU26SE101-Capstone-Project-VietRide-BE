namespace VietRide.Identity.Api.Controllers;

internal static class ClientKindClassifier
{
    public static string Classify(string? userAgent)
    {
        if (string.IsNullOrWhiteSpace(userAgent))
            return "UNKNOWN";

        if (ContainsAny(userAgent, "Android", "iPhone", "iPad", "Dart", "okhttp"))
            return "MOBILE";

        if (ContainsAny(userAgent, "Mozilla", "Chrome", "Safari", "Firefox", "Edge"))
            return "WEB";

        if (ContainsAny(userAgent, "Postman", "curl", "Swagger", "Insomnia"))
            return "API_CLIENT";

        return "UNKNOWN";
    }

    private static bool ContainsAny(string value, params string[] candidates)
        => candidates.Any(candidate => value.Contains(candidate, StringComparison.OrdinalIgnoreCase));
}
