using FluentAssertions;
using FluentValidation;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Features.Vouchers.ListVouchers;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Booking.UnitTests.Features.Vouchers;

/// <summary>
/// Unit tests for <see cref="ListVouchersQueryHandler"/>.
/// </summary>
public class ListVouchersQueryHandlerTests
{
    private static readonly Guid OperatorId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid AdminUserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private readonly IVoucherRepository _vouchers = Substitute.For<IVoucherRepository>();

    private ListVouchersQueryHandler BuildSut() => new(_vouchers);

    private static Voucher CreateVoucher(
        string code,
        VoucherFundingType funding = VoucherFundingType.VIETRIDE_FUNDED,
        Guid? ownerOperatorId = null) =>
        Voucher.Create(
            code: code,
            name: "Test Voucher",
            type: VoucherType.FIXED_AMOUNT,
            value: 10_000,
            minOrderAmount: Money.FromRaw(0),
            maxDiscountAmount: null,
            totalUsageLimit: null,
            perUserLimit: null,
            validFrom: DateTimeOffset.UtcNow.AddDays(-1),
            validUntil: DateTimeOffset.UtcNow.AddDays(10),
            applicableOperatorIds: ownerOperatorId.HasValue ? [ownerOperatorId.Value] : null,
            applicableRouteIds: null,
            fundingType: funding,
            ownerOperatorId: ownerOperatorId,
            createdByUserId: AdminUserId);

    // -----------------------------------------------------------------------
    // Happy path — unfiltered returns all vouchers paged
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_Unfiltered_ReturnsPaged()
    {
        // Arrange
        var voucher1 = CreateVoucher("CODE1");
        var voucher2 = CreateVoucher("CODE2");
        var items = new List<Voucher> { voucher1, voucher2 };

        _vouchers.ListAsync(
                ownerOperatorId: null,
                fundingType: null,
                isActive: null,
                page: 1,
                pageSize: 20,
                sortBy: null,
                sortDir: "desc",
                ct: Arg.Any<CancellationToken>())
            .Returns((items.AsReadOnly(), 2L));

        var sut = BuildSut();
        var query = new ListVouchersQuery(
            OwnerOperatorId: null,
            FundingType: null,
            IsActive: null,
            Options: new QueryOptions { Page = 1, PageSize = 20 });

        // Act
        var result = await sut.Handle(query, CancellationToken.None);

        // Assert
        result.Items.Should().HaveCount(2);
        result.TotalItems.Should().Be(2);
        result.Page.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Filter by ownerOperatorId
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_FilterByOwnerOperatorId_ReturnsOperatorVouchers()
    {
        // Arrange
        var operatorVoucher = CreateVoucher(
            "OPCODE1",
            VoucherFundingType.OPERATOR_FUNDED,
            ownerOperatorId: OperatorId);

        var items = new List<Voucher> { operatorVoucher };

        _vouchers.ListAsync(
                ownerOperatorId: OperatorId,
                fundingType: null,
                isActive: null,
                page: 1,
                pageSize: 20,
                sortBy: null,
                sortDir: "desc",
                ct: Arg.Any<CancellationToken>())
            .Returns((items.AsReadOnly(), 1L));

        var sut = BuildSut();
        var query = new ListVouchersQuery(
            OwnerOperatorId: OperatorId,
            FundingType: null,
            IsActive: null,
            Options: new QueryOptions { Page = 1, PageSize = 20 });

        // Act
        var result = await sut.Handle(query, CancellationToken.None);

        // Assert
        result.Items.Should().HaveCount(1);
        result.Items[0].Code.Should().Be("OPCODE1");
        result.Items[0].OwnerOperatorId.Should().Be(OperatorId);
        result.TotalItems.Should().Be(1);
    }

    // -----------------------------------------------------------------------
    // Validator — non-whitelisted sortBy → INVALID_SORT_FIELD
    // -----------------------------------------------------------------------

    [Fact]
    public void Validator_InvalidSortBy_FailsWithInvalidSortField()
    {
        // Arrange
        var validator = new ListVouchersQueryValidator();
        var query = new ListVouchersQuery(
            OwnerOperatorId: null,
            FundingType: null,
            IsActive: null,
            Options: new QueryOptions { Page = 1, PageSize = 20, SortBy = "nonExistentField" });

        // Act
        var result = validator.Validate(query);

        // Assert
        result.IsValid.Should().BeFalse();
        result.Errors.Should().ContainSingle(e => e.ErrorCode == "INVALID_SORT_FIELD");
    }

    [Fact]
    public void Validator_WhitelistedSortBy_Passes()
    {
        // Arrange
        var validator = new ListVouchersQueryValidator();
        var query = new ListVouchersQuery(
            OwnerOperatorId: null,
            FundingType: null,
            IsActive: null,
            Options: new QueryOptions { Page = 1, PageSize = 20, SortBy = "createdAt" });

        // Act
        var result = validator.Validate(query);

        // Assert
        result.IsValid.Should().BeTrue();
    }

    // -----------------------------------------------------------------------
    // Filter by fundingType string
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_FilterByFundingType_ReturnsMatchingVouchers()
    {
        // Arrange
        var opFunded = CreateVoucher("OPFUND", VoucherFundingType.OPERATOR_FUNDED, OperatorId);
        var items = new List<Voucher> { opFunded };

        _vouchers.ListAsync(
                ownerOperatorId: null,
                fundingType: VoucherFundingType.OPERATOR_FUNDED,
                isActive: null,
                page: 1,
                pageSize: 20,
                sortBy: null,
                sortDir: "desc",
                ct: Arg.Any<CancellationToken>())
            .Returns((items.AsReadOnly(), 1L));

        var sut = BuildSut();
        // Pass fundingType as a string (matches controller → query flow)
        var query = new ListVouchersQuery(
            OwnerOperatorId: null,
            FundingType: "OPERATOR_FUNDED",
            IsActive: null,
            Options: new QueryOptions { Page = 1, PageSize = 20 });

        // Act
        var result = await sut.Handle(query, CancellationToken.None);

        // Assert
        result.Items.Should().HaveCount(1);
        result.Items[0].FundingType.Should().Be("OPERATOR_FUNDED");
    }
}
