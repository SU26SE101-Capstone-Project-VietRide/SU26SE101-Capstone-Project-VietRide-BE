using System.Net;
using System.Text.Json;
using FluentAssertions;
using VietRide.Identity.Application.Abstractions.Http;
using VietRide.Identity.Infrastructure.Http;

namespace VietRide.Identity.UnitTests.Infrastructure.Http;

public sealed class NotificationEmailClientTests
{
    private static NotificationEmailRequest SampleRequest() => new(
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
        HttpRequestMessage? seen = null;
        string? bodyJson = null;
        var handler = new StubHandler(async (req, ct) =>
        {
            seen = req;
            bodyJson = await req.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.Accepted);
        });
        var client = new NotificationEmailClient(new HttpClient(handler) { BaseAddress = new Uri("http://notification:3002") });

        await client.SendEmailAsync(SampleRequest());

        seen!.Method.Should().Be(HttpMethod.Post);
        seen.RequestUri!.AbsolutePath.Should().Be("/internal/v1/emails");

        using var parsed = JsonDocument.Parse(bodyJson!);
        var root = parsed.RootElement;
        root.GetProperty("toEmail").GetString().Should().Be("user@vietride.local");
        root.GetProperty("templateKey").GetString().Should().Be("AUTH_OTP");
        root.GetProperty("templateData").GetProperty("code").GetString().Should().Be("123456");
        // null notificationId is serialized so Notification's nullable field is explicit.
        root.GetProperty("notificationId").ValueKind.Should().Be(JsonValueKind.Null);
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
}
