global using VietRide.Identity.IntegrationTests.Testing;

using Microsoft.AspNetCore.Mvc.Testing;

namespace VietRide.Identity.IntegrationTests.Testing;

internal static class IdempotentHttpClientExtensions
{
    public static HttpClient CreateIdempotentClient<TEntryPoint>(
        this WebApplicationFactory<TEntryPoint> factory)
        where TEntryPoint : class
        => factory.CreateDefaultClient(new MutationIdempotencyKeyHandler());

    private sealed class MutationIdempotencyKeyHandler : DelegatingHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            if (IsMutation(request.Method)
                && !request.Headers.Contains("Idempotency-Key"))
            {
                request.Headers.TryAddWithoutValidation(
                    "Idempotency-Key",
                    Guid.NewGuid().ToString("D"));
            }

            return base.SendAsync(request, cancellationToken);
        }

        private static bool IsMutation(HttpMethod method)
            => method == HttpMethod.Post
                || method == HttpMethod.Put
                || method == HttpMethod.Patch
                || method == HttpMethod.Delete;
    }
}
