using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using NSubstitute.ExceptionExtensions;
using VietRide.Identity.Application.Abstractions.ExternalClients;
using VietRide.Identity.Application.Abstractions.Http;
using VietRide.Identity.Infrastructure.ExternalClients;
using VietRide.Identity.Infrastructure.Http;

namespace VietRide.Identity.UnitTests.Infrastructure.ExternalClients;

/// <summary>
/// Tests for <see cref="NotificationEmailService"/> — covers non-OTP transactional emails only.
/// OTP delivery was migrated to Outbox (identity.otp.requested) in the async-OTP change;
/// <see cref="IEmailService"/> no longer has a SendOtpAsync method.
/// </summary>
public sealed class NotificationEmailServiceTests
{
    private readonly INotificationEmailClient _client = Substitute.For<INotificationEmailClient>();
    private readonly NotificationEmailService _sut;

    public NotificationEmailServiceTests()
    {
        _sut = new NotificationEmailService(_client, NullLogger<NotificationEmailService>.Instance);
    }

    // -------------------------------------------------------------------------
    // Happy path — account-created / set-initial-password
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SendAccountCreatedLinkAsync_PostsSetInitialPasswordTemplate()
    {
        var userId = Guid.NewGuid();
        var expiresAt = DateTimeOffset.UtcNow.AddHours(24);
        NotificationEmailRequest? captured = null;
        await _client.SendEmailAsync(Arg.Do<NotificationEmailRequest>(r => captured = r), Arg.Any<CancellationToken>());

        await _sut.SendAccountCreatedLinkAsync(
            "staff@vietride.local",
            new AccountCreatedEmailDto(userId, "Staff Member", "https://app.vietride.app/auth/set-password?token=abc", expiresAt));

        captured.Should().NotBeNull();
        captured!.TemplateKey.Should().Be("SET_INITIAL_PASSWORD");
        captured.ToEmail.Should().Be("staff@vietride.local");
        captured.TemplateData.Should().Contain("userId", userId);
        captured.TemplateData.Should().Contain("displayName", "Staff Member");
        captured.TemplateData["setInitialPasswordUrl"].Should().Be("https://app.vietride.app/auth/set-password?token=abc");
        captured.TemplateData.Should().Contain("expiresAt", expiresAt);
    }

    [Fact]
    public async Task SendAccountCreatedLinkAsync_WhenNotificationRejects_PropagatesDeliveryException()
    {
        _client.SendEmailAsync(Arg.Any<NotificationEmailRequest>(), Arg.Any<CancellationToken>())
            .Throws(new NotificationEmailDeliveryException("status 503"));

        var act = () => _sut.SendAccountCreatedLinkAsync(
            "staff@vietride.local",
            new AccountCreatedEmailDto(Guid.NewGuid(), "Staff", "https://url", DateTimeOffset.UtcNow.AddHours(1)));

        await act.Should().ThrowAsync<NotificationEmailDeliveryException>();
    }

    [Fact]
    public async Task SendAccountCreatedLinkAsync_WhenTransportFails_WrapsAsDeliveryException()
    {
        _client.SendEmailAsync(Arg.Any<NotificationEmailRequest>(), Arg.Any<CancellationToken>())
            .Throws(new HttpRequestException("connection refused"));

        var act = () => _sut.SendAccountCreatedLinkAsync(
            "staff@vietride.local",
            new AccountCreatedEmailDto(Guid.NewGuid(), "Staff", "https://url", DateTimeOffset.UtcNow.AddHours(1)));

        var assertion = await act.Should().ThrowAsync<NotificationEmailDeliveryException>();
        assertion.Which.InnerException.Should().BeOfType<HttpRequestException>();
    }

    // -------------------------------------------------------------------------
    // Parcel delivery (Day 26+ stub)
    // -------------------------------------------------------------------------

    [Fact]
    public async Task SendParcelDeliveryLinkAsync_StillThrowsNotImplemented()
    {
        var act = () => _sut.SendParcelDeliveryLinkAsync(
            "to@vietride.local",
            "token",
            new ParcelDeliveryEmailDto("Sender", "Recipient", "Origin", "Destination", "2026-06-22T10:00:00Z", null, "2026-06-23T10:00:00Z"));

        await act.Should().ThrowAsync<NotImplementedException>();
    }
}
