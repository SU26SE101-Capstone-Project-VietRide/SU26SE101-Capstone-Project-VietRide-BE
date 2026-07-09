using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.Services;
using VietRide.Booking.Application.Features.OperatorVouchers.CreateOperatorVoucher;
using VietRide.Booking.Domain.Entities;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Booking.UnitTests.Features.OperatorVouchers;

/// <summary>
/// Unit tests for <see cref="CreateOperatorVoucherCommandHandler"/>.
/// </summary>
public class CreateOperatorVoucherCommandHandlerTests
{
    // -----------------------------------------------------------------------
    // Fixtures
    // -----------------------------------------------------------------------

    private static readonly Guid OperatorId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OperatorUserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private readonly IVoucherRepository _vouchers = Substitute.For<IVoucherRepository>();
    private readonly IVoucherCodeGenerator _codeGenerator = Substitute.For<IVoucherCodeGenerator>();
    private readonly IClock _clock = Substitute.For<IClock>();

    private CreateOperatorVoucherCommandHandler BuildSut() => new(
        _vouchers,
        _codeGenerator,
        _clock,
        NullLogger<CreateOperatorVoucherCommandHandler>.Instance);

    private static CreateOperatorVoucherCommand BuildCommand(
        string? code = "OPVCH001",
        string? fundingType = null,
        IReadOnlyList<string>? applicableServices = null) =>
        new(
            Code: code,
            Name: "Operator Discount",
            Type: "PERCENT_OFF",
            Value: 10,
            MinOrderAmount: 50_000,
            MaxDiscountAmount: 30_000,
            TotalUsageLimit: 50,
            PerUserLimit: 1,
            ValidFrom: DateTimeOffset.UtcNow.AddDays(1),
            ValidUntil: DateTimeOffset.UtcNow.AddDays(30),
            ApplicableServices: applicableServices,
            ApplicableRouteIds: null,
            FundingType: fundingType,
            OwnerOperatorId: OperatorId,
            CreatedByUserId: OperatorUserId);

    // -----------------------------------------------------------------------
    // Happy path — operator-owned voucher created with correct owner
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_ValidCommand_CreatesOperatorOwnedVoucherWithNoConsentRows()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        _clock.UtcNow.Returns(now);
        _vouchers.CodeExistsAsync("OPVCH001", Arg.Any<CancellationToken>())
            .Returns(false);

        Voucher? capturedVoucher = null;
        _vouchers.AddAsync(
            Arg.Do<Voucher>(v => capturedVoucher = v),
            Arg.Any<CancellationToken>())
            .Returns(args => args.Arg<Voucher>());

        var sut = BuildSut();
        var command = BuildCommand("OPVCH001");

        // Act
        var result = await sut.Handle(command, CancellationToken.None);

        // Assert
        result.Code.Should().Be("OPVCH001");
        result.OwnerOperatorId.Should().Be(OperatorId);
        result.FundingType.Should().Be("OPERATOR_FUNDED");
        result.IsActive.Should().BeTrue();

        // applicableOperatorIds FORCED to [caller operatorId] server-side — not request-supplied
        capturedVoucher.Should().NotBeNull();
        capturedVoucher!.ApplicableOperatorIds.Should().ContainSingle()
            .Which.Should().Be(OperatorId,
                "applicableOperatorIds must be forced to [ownerOperatorId] server-side, never request-supplied");
        capturedVoucher.ApplicableServices.Should().Equal("BOOKING");

        // No consent fan-out — operator-created vouchers are self-consented
        await _vouchers.DidNotReceive()
            .AddConsentAsync(Arg.Any<OperatorVoucherConsent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_ParcelApplicableServices_CreatesParcelVoucher()
    {
        var now = DateTimeOffset.UtcNow;
        _clock.UtcNow.Returns(now);
        _vouchers.CodeExistsAsync("OPVCH001", Arg.Any<CancellationToken>())
            .Returns(false);

        Voucher? capturedVoucher = null;
        _vouchers.AddAsync(
            Arg.Do<Voucher>(v => capturedVoucher = v),
            Arg.Any<CancellationToken>())
            .Returns(args => args.Arg<Voucher>());

        var sut = BuildSut();
        var command = BuildCommand("OPVCH001", applicableServices: ["PARCEL"]);

        var result = await sut.Handle(command, CancellationToken.None);

        result.Code.Should().Be("OPVCH001");
        capturedVoucher.Should().NotBeNull();
        capturedVoucher!.ApplicableServices.Should().Equal("PARCEL");
    }

    // -----------------------------------------------------------------------
    // Error path — fundingType = VIETRIDE_FUNDED → 422 VOUCHER_FORBIDDEN_FUNDING
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_FundingTypeVietrideFunded_ThrowsVoucherForbiddenFunding()
    {
        // Arrange
        var sut = BuildSut();
        var command = BuildCommand(fundingType: "VIETRIDE_FUNDED");

        // Act
        var act = () => sut.Handle(command, CancellationToken.None);

        // Assert
        var ex = await act.Should().ThrowAsync<CodedValidationException>();
        ex.Which.ErrorCode.Should().Be("VOUCHER_FORBIDDEN_FUNDING");
    }

    // -----------------------------------------------------------------------
    // Error path — duplicate code → 409 VOUCHER_CODE_CONFLICT
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_DuplicateCode_ThrowsVoucherCodeConflict()
    {
        // Arrange
        _vouchers.CodeExistsAsync("OPVCH001", Arg.Any<CancellationToken>())
            .Returns(true);

        var sut = BuildSut();
        var command = BuildCommand("OPVCH001");

        // Act
        var act = () => sut.Handle(command, CancellationToken.None);

        // Assert
        var ex = await act.Should().ThrowAsync<CodedConflictException>();
        ex.Which.ErrorCode.Should().Be("VOUCHER_CODE_CONFLICT");
    }

    // -----------------------------------------------------------------------
    // Auto-generate code path
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_NullCode_AutoGeneratesUniqueCode()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        _clock.UtcNow.Returns(now);
        _codeGenerator.Generate().Returns("AUTOOP01");
        _vouchers.CodeExistsAsync("AUTOOP01", Arg.Any<CancellationToken>())
            .Returns(false);
        _vouchers.AddAsync(Arg.Any<Voucher>(), Arg.Any<CancellationToken>())
            .Returns(args => args.Arg<Voucher>());

        var sut = BuildSut();
        var command = BuildCommand(code: null);

        // Act
        var result = await sut.Handle(command, CancellationToken.None);

        // Assert
        result.Code.Should().Be("AUTOOP01");
        _codeGenerator.Received(1).Generate();
    }
}
