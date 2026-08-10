namespace VietRide.Payment.Application.Abstractions.ExternalClients;

public sealed record VnPaySdkConfiguration(
    string TmnCode,
    string Scheme,
    bool IsSandbox);
