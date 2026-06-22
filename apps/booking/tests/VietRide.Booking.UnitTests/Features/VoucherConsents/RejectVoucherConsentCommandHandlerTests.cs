using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Features.VoucherConsents.RejectVoucherConsent;
using VietRide.Booking.Domain.Entities;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Booking.UnitTests.Features.VoucherConsents;

/// <summary>
/// Unit tests for <see cref="RejectVoucherConsentCommandHandler"/>.
/// </summary>
public class RejectVoucherConsentCommandHandlerTests
{
    // -----------------------------------------------------------------------
    // Fixtures
    // -----------------------------------------------------------------------

    private static readonly Guid OperatorId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OperatorUserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid VoucherId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid ConsentId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");

    private readonly IOperatorVoucherConsentRepository _consents =
        Substitute.For<IOperatorVoucherConsentRepository>();

    private readonly IIntegrationEventOutbox _outbox =
        Substitute.For<IIntegrationEventOutbox>();

    private readonly IClock _clock = Substitute.For<IClock>();

    private RejectVoucherConsentCommandHandler BuildSut() => new(
        _consents,
        _outbox,
        _clock,
        NullLogger<RejectVoucherConsentCommandHandler>.Instance);

    private static OperatorVoucherConsent BuildPendingConsent()
        => OperatorVoucherConsent.Create(OperatorId, VoucherId, DateTimeOffset.UtcNow.AddDays(-1));

    private static RejectVoucherConsentCommand BuildCommand(string? reason = null) =>
        new(ConsentId: ConsentId, CallerOperatorId: OperatorId, CallerUserId: OperatorUserId, Reason: reason);

    // -----------------------------------------------------------------------
    // Happy path — PENDING consent rejected, event enqueued
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_PendingConsent_RejectsAndEmitsEvent()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        _clock.UtcNow.Returns(now);

        var consent = BuildPendingConsent();
        _consents.FindByIdAndOperatorAsync(ConsentId, OperatorId, Arg.Any<CancellationToken>())
            .Returns(consent);

        var sut = BuildSut();

        // Act
        var result = await sut.Handle(BuildCommand("Not interested"), CancellationToken.None);

        // Assert — result shape is { id, status } per API contract
        result.Id.Should().NotBeEmpty();
        result.Status.Should().Be("REJECTED");

        await _outbox.Received(1)
            .EnqueueAsync(
                "booking.voucher.consent_rejected",
                Arg.Any<string>(),
                Arg.Any<CancellationToken>());
    }

    // -----------------------------------------------------------------------
    // Happy path — ACCEPTED consent revoked (ACCEPTED → REJECTED)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_AcceptedConsent_RevokesSuccessfully()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        _clock.UtcNow.Returns(now);

        var consent = BuildPendingConsent();
        consent.Accept(OperatorUserId, now.AddDays(-1));

        _consents.FindByIdAndOperatorAsync(ConsentId, OperatorId, Arg.Any<CancellationToken>())
            .Returns(consent);

        var sut = BuildSut();

        // Act
        var result = await sut.Handle(BuildCommand(), CancellationToken.None);

        // Assert — result shape is { id, status } per API contract
        result.Id.Should().NotBeEmpty();
        result.Status.Should().Be("REJECTED");
        await _outbox.Received(1)
            .EnqueueAsync(
                "booking.voucher.consent_rejected",
                Arg.Any<string>(),
                Arg.Any<CancellationToken>());
    }

    // -----------------------------------------------------------------------
    // Error path — cross-operator → 403 FORBIDDEN (tenant isolation)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_CrossOperatorConsent_ThrowsForbidden()
    {
        // Arrange
        _consents.FindByIdAndOperatorAsync(ConsentId, OperatorId, Arg.Any<CancellationToken>())
            .Returns((OperatorVoucherConsent?)null);

        var sut = BuildSut();

        // Act
        var act = () => sut.Handle(BuildCommand(), CancellationToken.None);

        // Assert
        var ex = await act.Should().ThrowAsync<ForbiddenException>();
        ex.Which.ErrorCode.Should().Be("FORBIDDEN");
    }

    // -----------------------------------------------------------------------
    // Error path — consent already REJECTED → 409 CONSENT_ALREADY_REJECTED
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_AlreadyRejectedConsent_ThrowsConsentAlreadyRejected()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        _clock.UtcNow.Returns(now);

        var consent = BuildPendingConsent();
        consent.Reject(OperatorUserId, now.AddDays(-1), "Already rejected");

        _consents.FindByIdAndOperatorAsync(ConsentId, OperatorId, Arg.Any<CancellationToken>())
            .Returns(consent);

        var sut = BuildSut();

        // Act
        var act = () => sut.Handle(BuildCommand(), CancellationToken.None);

        // Assert
        var ex = await act.Should().ThrowAsync<CodedConflictException>();
        ex.Which.ErrorCode.Should().Be("CONSENT_ALREADY_REJECTED");
    }
}
