using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Features.VoucherConsents.AcceptVoucherConsent;
using VietRide.Booking.Domain.Entities;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Booking.UnitTests.Features.VoucherConsents;

/// <summary>
/// Unit tests for <see cref="AcceptVoucherConsentCommandHandler"/>.
/// </summary>
public class AcceptVoucherConsentCommandHandlerTests
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

    private AcceptVoucherConsentCommandHandler BuildSut() => new(
        _consents,
        _outbox,
        _clock,
        NullLogger<AcceptVoucherConsentCommandHandler>.Instance);

    private static OperatorVoucherConsent BuildPendingConsent()
        => OperatorVoucherConsent.Create(OperatorId, VoucherId, DateTimeOffset.UtcNow.AddDays(-1));

    private static AcceptVoucherConsentCommand BuildCommand() =>
        new(ConsentId: ConsentId, CallerOperatorId: OperatorId, CallerUserId: OperatorUserId);

    // -----------------------------------------------------------------------
    // Happy path — PENDING consent accepted, event enqueued
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_PendingConsent_AcceptsAndEmitsEvent()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        _clock.UtcNow.Returns(now);

        var consent = BuildPendingConsent();
        _consents.FindByIdAndOperatorAsync(ConsentId, OperatorId, Arg.Any<CancellationToken>())
            .Returns(consent);

        var sut = BuildSut();

        // Act
        var result = await sut.Handle(BuildCommand(), CancellationToken.None);

        // Assert — result shape is { id, status } per API contract
        result.Id.Should().NotBeEmpty();
        result.Status.Should().Be("ACCEPTED");

        await _outbox.Received(1)
            .EnqueueAsync(
                "booking.voucher.consent_accepted",
                Arg.Any<string>(),
                Arg.Any<CancellationToken>());

        _consents.Received(1).Update(consent);
    }

    // -----------------------------------------------------------------------
    // Error path — cross-operator → 403 FORBIDDEN
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
    // Error path — consent already ACCEPTED → 409 CONSENT_NOT_PENDING
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_AlreadyAcceptedConsent_ThrowsConsentNotPending()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        _clock.UtcNow.Returns(now);

        var consent = BuildPendingConsent();
        consent.Accept(OperatorUserId, now); // pre-accept

        _consents.FindByIdAndOperatorAsync(ConsentId, OperatorId, Arg.Any<CancellationToken>())
            .Returns(consent);

        var sut = BuildSut();

        // Act
        var act = () => sut.Handle(BuildCommand(), CancellationToken.None);

        // Assert
        var ex = await act.Should().ThrowAsync<CodedConflictException>();
        ex.Which.ErrorCode.Should().Be("CONSENT_NOT_PENDING");
    }

    // -----------------------------------------------------------------------
    // Error path — consent already REJECTED → 409 CONSENT_NOT_PENDING
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_AlreadyRejectedConsent_ThrowsConsentNotPending()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        _clock.UtcNow.Returns(now);

        var consent = BuildPendingConsent();
        consent.Reject(OperatorUserId, now); // pre-reject

        _consents.FindByIdAndOperatorAsync(ConsentId, OperatorId, Arg.Any<CancellationToken>())
            .Returns(consent);

        var sut = BuildSut();

        // Act
        var act = () => sut.Handle(BuildCommand(), CancellationToken.None);

        // Assert
        var ex = await act.Should().ThrowAsync<CodedConflictException>();
        ex.Which.ErrorCode.Should().Be("CONSENT_NOT_PENDING");
    }
}
