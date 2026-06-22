using FluentAssertions;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Features.VoucherConsents.ListVoucherConsents;
using VietRide.Booking.Domain.Entities;

namespace VietRide.Booking.UnitTests.Features.VoucherConsents;

/// <summary>
/// Unit tests for <see cref="ListAdminVoucherConsentsQueryHandler"/>.
/// </summary>
public class ListAdminVoucherConsentsQueryHandlerTests
{
    // -----------------------------------------------------------------------
    // Fixtures
    // -----------------------------------------------------------------------

    private static readonly Guid VoucherId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid OtherVoucherId = Guid.Parse("dddddddd-dddd-dddd-dddd-dddddddddddd");
    private static readonly Guid OperatorIdA = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OperatorIdB = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private readonly IOperatorVoucherConsentRepository _consents =
        Substitute.For<IOperatorVoucherConsentRepository>();

    private ListAdminVoucherConsentsQueryHandler BuildSut() =>
        new(_consents);

    private static OperatorVoucherConsent BuildConsent(Guid operatorId, Guid voucherId) =>
        OperatorVoucherConsent.Create(operatorId, voucherId, DateTimeOffset.UtcNow.AddDays(-2));

    // -----------------------------------------------------------------------
    // Happy path — returns consent rows for a given voucherId
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_ValidVoucherId_ReturnsConsentRowsForThatVoucher()
    {
        // Arrange
        var consentA = BuildConsent(OperatorIdA, VoucherId);
        var consentB = BuildConsent(OperatorIdB, VoucherId);

        _consents
            .ListByVoucherAsync(VoucherId, Arg.Any<CancellationToken>())
            .Returns(new List<OperatorVoucherConsent> { consentA, consentB });

        var sut = BuildSut();
        var query = new ListAdminVoucherConsentsQuery(VoucherId: VoucherId);

        // Act
        var result = await sut.Handle(query, CancellationToken.None);

        // Assert
        result.VoucherId.Should().Be(VoucherId);
        result.Items.Should().HaveCount(2);
        result.Items.Should().AllSatisfy(item => item.VoucherId.Should().Be(VoucherId));
        result.Items.Select(i => i.OperatorId).Should().BeEquivalentTo(new[] { OperatorIdA, OperatorIdB });
    }

    // -----------------------------------------------------------------------
    // Happy path — voucher with no consents returns empty Items list
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_VoucherWithNoConsents_ReturnsEmptyItems()
    {
        // Arrange
        _consents
            .ListByVoucherAsync(VoucherId, Arg.Any<CancellationToken>())
            .Returns(new List<OperatorVoucherConsent>());

        var sut = BuildSut();
        var query = new ListAdminVoucherConsentsQuery(VoucherId: VoucherId);

        // Act
        var result = await sut.Handle(query, CancellationToken.None);

        // Assert
        result.VoucherId.Should().Be(VoucherId);
        result.Items.Should().BeEmpty();
    }

    // -----------------------------------------------------------------------
    // Happy path — consent item fields are mapped correctly
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_ConsentRows_MapsStatusAndRejectReasonCorrectly()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        var operatorUserId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");

        var consent = BuildConsent(OperatorIdA, VoucherId);
        consent.Reject(operatorUserId, now, "Not interested");

        _consents
            .ListByVoucherAsync(VoucherId, Arg.Any<CancellationToken>())
            .Returns(new List<OperatorVoucherConsent> { consent });

        var sut = BuildSut();
        var query = new ListAdminVoucherConsentsQuery(VoucherId: VoucherId);

        // Act
        var result = await sut.Handle(query, CancellationToken.None);

        // Assert
        result.Items.Should().HaveCount(1);
        var item = result.Items[0];
        item.Status.Should().Be("REJECTED");
        item.RespondedByUserId.Should().Be(operatorUserId);
        item.RespondedAt.Should().Be(now);
        item.RejectReason.Should().Be("Not interested");
    }

    // -----------------------------------------------------------------------
    // Scoping — repository is queried with the exact voucherId from the query
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_CallsRepositoryWithCorrectVoucherId()
    {
        // Arrange
        _consents
            .ListByVoucherAsync(VoucherId, Arg.Any<CancellationToken>())
            .Returns(new List<OperatorVoucherConsent>());

        var sut = BuildSut();
        var query = new ListAdminVoucherConsentsQuery(VoucherId: VoucherId);

        // Act
        await sut.Handle(query, CancellationToken.None);

        // Assert — only called with the exact voucherId, never OtherVoucherId.
        await _consents.Received(1)
            .ListByVoucherAsync(VoucherId, Arg.Any<CancellationToken>());

        await _consents.DidNotReceive()
            .ListByVoucherAsync(OtherVoucherId, Arg.Any<CancellationToken>());
    }
}
