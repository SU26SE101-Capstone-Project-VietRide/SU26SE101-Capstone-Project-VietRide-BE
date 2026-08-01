using Microsoft.Extensions.Options;
using VietRide.Payment.Application.Abstractions.Services;

namespace VietRide.Payment.Infrastructure.VnPay;

public sealed class VnPayRedirectUrlValidator : IVnPayRedirectUrlValidator
{
    private readonly Uri? _baseUri;

    public VnPayRedirectUrlValidator(IOptions<VnPayOptions> options)
    {
        var baseUrl = options.Value.BaseUrl;
        if (Uri.TryCreate(baseUrl, UriKind.Absolute, out var baseUri)
            && baseUri.Scheme == Uri.UriSchemeHttps
            && string.IsNullOrEmpty(baseUri.UserInfo))
        {
            _baseUri = baseUri;
        }
    }

    public bool IsTrusted(string? paymentRedirectUrl)
        => !string.IsNullOrWhiteSpace(paymentRedirectUrl)
            && _baseUri is not null
            && Uri.TryCreate(paymentRedirectUrl, UriKind.Absolute, out var redirectUri)
            && redirectUri.Scheme == Uri.UriSchemeHttps
            && string.IsNullOrEmpty(redirectUri.UserInfo)
            && string.Equals(redirectUri.Authority, _baseUri.Authority, StringComparison.OrdinalIgnoreCase);
}
