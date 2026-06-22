using FluentAssertions;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Features.VoucherConsents.ListVoucherConsents;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Booking.UnitTests.Features.VoucherConsents;

/// <summary>
/// Unit tests for <see cref="ListVoucherConsentsQueryHandler"/>.
/// </summary>
public class ListVoucherConsentsQueryHandlerTests
{
    // -----------------------------------------------------------------------
    // Fixtures
    // -----------------------------------------------------------------------

    private static readonly Guid OperatorId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherOperatorId = Guid.Parse("eeeeeeee-eeee-eeee-eeee-eeeeeeeeeeee");
    private static readonly Guid VoucherId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid AdminUserId = Guid.Parse("ffffffff-ffff-ffff-ffff-ffffffffffff");

    private readonly IOperatorVoucherConsentRepository _consents =
        Substitute.For<IOperatorVoucherConsentRepository>();

    private ListVoucherConsentsQueryHandler BuildSut() =>
        new(_consents);

    private static Voucher BuildVoucher()
    {
        var now = DateTimeOffset.UtcNow;
        return Voucher.Create(
            code: "TESTVCH1",
            name: "Test Voucher",
            type: VoucherType.FIXED_AMOUNT,
            value: 50_000,
            minOrderAmount: Money.FromRaw(100_000),
            maxDiscountAmount: null,
            totalUsageLimit: null,
            perUserLimit: null,
            validFrom: now.AddDays(-1),
            validUntil: now.AddDays(30),
            applicableOperatorIds: null,
            applicableRouteIds: null,
            fundingType: VoucherFundingType.VIETRIDE_FUNDED,
            ownerOperatorId: null,
            createdByUserId: AdminUserId);
    }

    /// <summary>
    /// Creates a pending consent and injects the Voucher navigation property via reflection
    /// to simulate EF Include loading (navigation is private set in the domain entity).
    /// </summary>
    private static OperatorVoucherConsent BuildPendingConsentWithVoucher(Guid operatorId, Voucher voucher)
    {
        var consent = OperatorVoucherConsent.Create(operatorId, voucher.Id, DateTimeOffset.UtcNow.AddDays(-1));
        typeof(OperatorVoucherConsent)
            .GetProperty(nameof(OperatorVoucherConsent.Voucher))!
            .SetValue(consent, voucher);
        return consent;
    }

    // -----------------------------------------------------------------------
    // Happy path — returns operator-scoped consent items filtered by status
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_WithStatusFilter_ReturnsScopedItems()
    {
        // Arrange
        var voucher = BuildVoucher();
        var consent = BuildPendingConsentWithVoucher(OperatorId, voucher);

        _consents
            .ListByOperatorAsync(OperatorId, OperatorVoucherConsentStatus.PENDING, Arg.Any<CancellationToken>())
            .Returns(new List<OperatorVoucherConsent> { consent });

        var sut = BuildSut();
        var query = new ListVoucherConsentsQuery(CallerOperatorId: OperatorId, Status: "PENDING");

        // Act
        var result = await sut.Handle(query, CancellationToken.None);

        // Assert
        result.Items.Should().HaveCount(1);
        result.Items[0].VoucherId.Should().Be(voucher.Id);
        result.Items[0].VoucherCode.Should().Be(voucher.Code);
        result.Items[0].Status.Should().Be("PENDING");

        await _consents.Received(1)
            .ListByOperatorAsync(OperatorId, OperatorVoucherConsentStatus.PENDING, Arg.Any<CancellationToken>());
    }

    // -----------------------------------------------------------------------
    // Happy path — no status filter returns all consents for the operator
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_NoStatusFilter_ReturnsAllConsentsForOperator()
    {
        // Arrange
        var voucher = BuildVoucher();
        var consent = BuildPendingConsentWithVoucher(OperatorId, voucher);

        _consents
            .ListByOperatorAsync(OperatorId, null, Arg.Any<CancellationToken>())
            .Returns(new List<OperatorVoucherConsent> { consent });

        var sut = BuildSut();
        var query = new ListVoucherConsentsQuery(CallerOperatorId: OperatorId, Status: null);

        // Act
        var result = await sut.Handle(query, CancellationToken.None);

        // Assert
        result.Items.Should().HaveCount(1);

        await _consents.Received(1)
            .ListByOperatorAsync(OperatorId, null, Arg.Any<CancellationToken>());
    }

    // -----------------------------------------------------------------------
    // Tenant scoping — only the caller operator's consents are queried
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_TenantScoping_QueriesOnlyCallerOperatorId()
    {
        // Arrange
        _consents
            .ListByOperatorAsync(OperatorId, null, Arg.Any<CancellationToken>())
            .Returns(new List<OperatorVoucherConsent>());

        var sut = BuildSut();
        var query = new ListVoucherConsentsQuery(CallerOperatorId: OperatorId, Status: null);

        // Act
        await sut.Handle(query, CancellationToken.None);

        // Assert — the repository is called with the caller's operatorId, never another operator's id.
        await _consents.Received(1)
            .ListByOperatorAsync(OperatorId, Arg.Any<OperatorVoucherConsentStatus?>(), Arg.Any<CancellationToken>());

        await _consents.DidNotReceive()
            .ListByOperatorAsync(OtherOperatorId, Arg.Any<OperatorVoucherConsentStatus?>(), Arg.Any<CancellationToken>());
    }

    // -----------------------------------------------------------------------
    // Error path — invalid status string → coded validation error INVALID_STATUS
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_InvalidStatusString_ThrowsCodedValidationException()
    {
        // Arrange
        var sut = BuildSut();
        var query = new ListVoucherConsentsQuery(CallerOperatorId: OperatorId, Status: "UNKNOWN_STATUS");

        // Act
        var act = () => sut.Handle(query, CancellationToken.None);

        // Assert
        var ex = await act.Should().ThrowAsync<CodedValidationException>();
        ex.Which.ErrorCode.Should().Be("INVALID_STATUS");
    }
}
