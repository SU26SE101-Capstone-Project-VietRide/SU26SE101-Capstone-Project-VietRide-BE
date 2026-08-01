namespace VietRide.Payment.Application.Abstractions.Services;

public interface IVnPayRedirectUrlValidator
{
    bool IsTrusted(string? paymentRedirectUrl);
}
