using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Abstractions.Services;
using VietRide.Booking.Application.Features.Vouchers.CreateVoucher;
using VietRide.Booking.Domain.Entities;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Application.Outbox;
using VietRide.Shared.Kernel.Abstractions;

namespace VietRide.Booking.UnitTests.Features.Vouchers;

/// <summary>
/// Unit tests for <see cref="CreateVoucherCommandHandler"/>.
/// </summary>
public class CreateVoucherCommandHandlerTests
{
    // -----------------------------------------------------------------------
    // Fixtures
    // -----------------------------------------------------------------------

    private static readonly Guid AdminUserId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OperatorId1 = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");
    private static readonly Guid OperatorId2 = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");

    private readonly IVoucherRepository _vouchers = Substitute.For<IVoucherRepository>();
    private readonly IVoucherCodeGenerator _codeGenerator = Substitute.For<IVoucherCodeGenerator>();
    private readonly IClock _clock = Substitute.For<IClock>();
    private readonly IIntegrationEventOutbox _outbox = Substitute.For<IIntegrationEventOutbox>();

    private CreateVoucherCommandHandler BuildSut() => new(
        _vouchers,
        _codeGenerator,
        _clock,
        _outbox,
        NullLogger<CreateVoucherCommandHandler>.Instance);

    private static CreateVoucherCommand BuildVietrideFundedCommand(string? code = "PROMO2024") =>
        new(
            Code: code,
            Name: "Summer Sale",
            Type: "PERCENT_OFF",
            Value: 10,
            MinOrderAmount: 100_000,
            MaxDiscountAmount: 50_000,
            TotalUsageLimit: 100,
            PerUserLimit: 1,
            ValidFrom: DateTimeOffset.UtcNow.AddDays(1),
            ValidUntil: DateTimeOffset.UtcNow.AddDays(30),
            ApplicableOperatorIds: null,
            ApplicableRouteIds: null,
            FundingType: "VIETRIDE_FUNDED",
            CreatedByUserId: AdminUserId);

    private static CreateVoucherCommand BuildOperatorFundedCommand(
        IReadOnlyList<Guid>? operatorIds = null) =>
        new(
            Code: "OPFUND01",
            Name: "Operator Special",
            Type: "FIXED_AMOUNT",
            Value: 20_000,
            MinOrderAmount: 50_000,
            MaxDiscountAmount: null,
            TotalUsageLimit: 50,
            PerUserLimit: null,
            ValidFrom: DateTimeOffset.UtcNow.AddDays(1),
            ValidUntil: DateTimeOffset.UtcNow.AddDays(15),
            ApplicableOperatorIds: operatorIds ?? [OperatorId1, OperatorId2],
            ApplicableRouteIds: null,
            FundingType: "OPERATOR_FUNDED",
            CreatedByUserId: AdminUserId);

    // -----------------------------------------------------------------------
    // Happy path — VIETRIDE_FUNDED voucher created
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_VietrideFunded_CreatesVoucherWithNullOwnerAndNoConsentRows()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        _clock.UtcNow.Returns(now);
        _vouchers.CodeExistsAsync("PROMO2024", Arg.Any<CancellationToken>())
            .Returns(false);
        _vouchers.AddAsync(Arg.Any<Voucher>(), Arg.Any<CancellationToken>())
            .Returns(args => args.Arg<Voucher>());

        var sut = BuildSut();
        var command = BuildVietrideFundedCommand("PROMO2024");

        // Act
        var result = await sut.Handle(command, CancellationToken.None);

        // Assert
        result.Code.Should().Be("PROMO2024");
        result.OwnerOperatorId.Should().BeNull();
        result.FundingType.Should().Be("VIETRIDE_FUNDED");
        result.IsActive.Should().BeTrue();
        result.Name.Should().Be("Summer Sale");

        // No consent fan-out for VIETRIDE_FUNDED
        await _vouchers.DidNotReceive()
            .AddConsentAsync(Arg.Any<OperatorVoucherConsent>(), Arg.Any<CancellationToken>());
    }

    // -----------------------------------------------------------------------
    // Happy path — OPERATOR_FUNDED with fan-out
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_OperatorFunded_CreatesConsentRowPerTargetedOperator()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        _clock.UtcNow.Returns(now);
        _vouchers.CodeExistsAsync("OPFUND01", Arg.Any<CancellationToken>())
            .Returns(false);
        _vouchers.AddAsync(Arg.Any<Voucher>(), Arg.Any<CancellationToken>())
            .Returns(args => args.Arg<Voucher>());
        _vouchers.AddConsentAsync(Arg.Any<OperatorVoucherConsent>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);
        var events = new List<(Guid EventId, string Payload)>();
        _outbox.EnqueueAsync(
                Arg.Any<Guid>(),
                "booking.voucher.consent_requested",
                Arg.Any<string>(),
                Arg.Any<CancellationToken>())
            .Returns(call =>
            {
                events.Add((call.ArgAt<Guid>(0), call.ArgAt<string>(2)));
                return Task.CompletedTask;
            });

        var sut = BuildSut();
        var command = BuildOperatorFundedCommand([OperatorId1, OperatorId2]);

        // Act
        var result = await sut.Handle(command, CancellationToken.None);

        // Assert
        result.FundingType.Should().Be("OPERATOR_FUNDED");
        result.OwnerOperatorId.Should().BeNull(); // admin-created

        // 2 consent rows fanned out — one per operator
        await _vouchers.Received(2)
            .AddConsentAsync(Arg.Any<OperatorVoucherConsent>(), Arg.Any<CancellationToken>());
        events.Should().HaveCount(2);
        events.Select(item =>
        {
            using var document = JsonDocument.Parse(item.Payload);
            var root = document.RootElement;
            root.GetProperty("eventId").GetGuid().Should().Be(item.EventId);
            root.GetProperty("voucherId").GetGuid().Should().Be(result.Id);
            root.GetProperty("voucherCode").GetString().Should().Be("OPFUND01");
            root.GetProperty("voucherType").GetString().Should().Be("FIXED_AMOUNT");
            root.GetProperty("voucherValue").GetInt64().Should().Be(20_000);
            return root.GetProperty("operatorId").GetGuid();
        }).Should().BeEquivalentTo([OperatorId1, OperatorId2]);
    }

    // -----------------------------------------------------------------------
    // Error path — duplicate code → VOUCHER_CODE_CONFLICT (409)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_DuplicateCode_ThrowsConflictException()
    {
        // Arrange
        _vouchers.CodeExistsAsync("PROMO2024", Arg.Any<CancellationToken>())
            .Returns(true);

        var sut = BuildSut();
        var command = BuildVietrideFundedCommand("PROMO2024");

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
        _codeGenerator.Generate().Returns("AUTOCOD1");
        _vouchers.CodeExistsAsync("AUTOCOD1", Arg.Any<CancellationToken>())
            .Returns(false);
        _vouchers.AddAsync(Arg.Any<Voucher>(), Arg.Any<CancellationToken>())
            .Returns(args => args.Arg<Voucher>());

        var sut = BuildSut();
        var command = BuildVietrideFundedCommand(code: null); // null triggers auto-gen

        // Act
        var result = await sut.Handle(command, CancellationToken.None);

        // Assert
        result.Code.Should().Be("AUTOCOD1");
        _codeGenerator.Received(1).Generate();
    }
}
