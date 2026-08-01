using System.Net;
using System.Text.Json;
using FluentAssertions;
using VietRide.Identity.Application.Abstractions.Http;
using VietRide.Identity.Infrastructure.Http;

namespace VietRide.Identity.UnitTests.Infrastructure.Http;

public sealed class NotificationEmailClientTests
{
    private static NotificationEmailRequest SampleRequest(Guid? idempotencyKey = null) => new(
        IdempotencyKey: idempotencyKey ?? Guid.NewGuid(),
        ToEmail: "user@vietride.local",
        TemplateKey: "AUTH_OTP",
        TemplateData: new Dictionary<string, object?>
        {
            ["code"] = "123456",
            ["purpose"] = "REGISTRATION",
            ["ttlMinutes"] = 5,
        });

    [Fact]
    public async Task SendEmailAsync_Posts202_SucceedsAndSendsExpectedBody()
    {
        var idempotencyKey = Guid.NewGuid();
        HttpRequestMessage? seen = null;
        string? seenIdempotencyKey = null;
        string? bodyJson = null;
        var handler = new StubHandler(async (req, ct) =>
        {
            seen = req;
            seenIdempotencyKey = req.Headers.GetValues("Idempotency-Key").Single();
            bodyJson = await req.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        });
        var client = new NotificationEmailClient(new HttpClient(handler) { BaseAddress = new Uri("http://notification:3002") });

        await client.SendEmailAsync(SampleRequest(idempotencyKey));

        seen!.Method.Should().Be(HttpMethod.Post);
        seen.RequestUri!.AbsolutePath.Should().Be("/internal/v1/emails");
        seenIdempotencyKey.Should().Be(idempotencyKey.ToString("D"));
        seenIdempotencyKey.Should().MatchRegex(
            "^[0-9a-f]{8}-[0-9a-f]{4}-4[0-9a-f]{3}-[89ab][0-9a-f]{3}-[0-9a-f]{12}$");

        using var parsed = JsonDocument.Parse(bodyJson!);
        var root = parsed.RootElement;
        root.EnumerateObject().Select(property => property.Name).Should().BeEquivalentTo(
            "notificationId",
            "toEmail",
            "templateKey",
            "templateData");
        root.GetProperty("toEmail").GetString().Should().Be("user@vietride.local");
        root.GetProperty("templateKey").GetString().Should().Be("AUTH_OTP");
        root.GetProperty("templateData").GetProperty("code").GetString().Should().Be("123456");
        // null notificationId is serialized so Notification's nullable field is explicit.
        root.GetProperty("notificationId").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task SendEmailAsync_WhenTransportRetries_ReusesSameIdempotencyKey()
    {
        var idempotencyKey = Guid.NewGuid();
        var seenKeys = new List<string>();
        var attempt = 0;
        var transport = new StubHandler((request, _) =>
        {
            seenKeys.Add(request.Headers.GetValues("Idempotency-Key").Single());
            attempt++;
            var status = attempt == 1
                ? HttpStatusCode.ServiceUnavailable
                : HttpStatusCode.Accepted;
            return Task.FromResult(new HttpResponseMessage(status));
        });
        var retry = new RetryOnceHandler { InnerHandler = transport };
        var client = new NotificationEmailClient(new HttpClient(retry) { BaseAddress = new Uri("http://notification:3002") });

        await client.SendEmailAsync(SampleRequest(idempotencyKey));

        seenKeys.Should().Equal(idempotencyKey.ToString("D"), idempotencyKey.ToString("D"));
    }

    [Theory]
    [InlineData(HttpStatusCode.InternalServerError)]
    [InlineData(HttpStatusCode.BadRequest)]
    [InlineData(HttpStatusCode.Unauthorized)]
    public async Task SendEmailAsync_NonSuccess_ThrowsDeliveryException(HttpStatusCode status)
    {
        var handler = new StubHandler((_, _) => Task.FromResult(new HttpResponseMessage(status)));
        var client = new NotificationEmailClient(new HttpClient(handler) { BaseAddress = new Uri("http://notification:3002") });

        var act = () => client.SendEmailAsync(SampleRequest());

        await act.Should().ThrowAsync<NotificationEmailDeliveryException>();
    }

    private sealed class StubHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> _handle;

        public StubHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> handle)
        {
            _handle = handle;
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
            => _handle(request, cancellationToken);
    }

    private sealed class RetryOnceHandler : DelegatingHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var firstResponse = await base.SendAsync(request, cancellationToken);
            if (firstResponse.StatusCode != HttpStatusCode.ServiceUnavailable)
                return firstResponse;

            firstResponse.Dispose();
            return await base.SendAsync(request, cancellationToken);
        }
    }
}
