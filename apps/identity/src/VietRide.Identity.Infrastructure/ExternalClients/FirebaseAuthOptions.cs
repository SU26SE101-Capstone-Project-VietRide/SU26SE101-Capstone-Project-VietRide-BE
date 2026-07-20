namespace VietRide.Identity.Infrastructure.ExternalClients;

public sealed class FirebaseAuthOptions
{
    public string ProjectId { get; init; } = string.Empty;
    public string ClientEmail { get; init; } = string.Empty;
    public string PrivateKey { get; init; } = string.Empty;

    public bool IsConfigured => !string.IsNullOrWhiteSpace(ProjectId)
        && !string.IsNullOrWhiteSpace(ClientEmail)
        && !string.IsNullOrWhiteSpace(PrivateKey);
}
