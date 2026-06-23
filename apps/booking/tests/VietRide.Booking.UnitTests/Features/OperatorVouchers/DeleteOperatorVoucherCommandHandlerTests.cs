using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Repositories;
using VietRide.Booking.Application.Features.OperatorVouchers.DeleteOperatorVoucher;
using VietRide.Booking.Domain.Entities;
using VietRide.Booking.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Abstractions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Booking.UnitTests.Features.OperatorVouchers;

/// <summary>
/// Unit tests for <see cref="DeleteOperatorVoucherCommandHandler"/>:
/// (a) happy-path soft-delete sets deleted_at;
/// (b) deleting an already-soft-deleted voucher is an idempotent no-op (success, not 404);
/// (c) cross-operator delete → 404 VOUCHER_NOT_FOUND.
/// </summary>
public class DeleteOperatorVoucherCommandHandlerTests
{
    // -----------------------------------------------------------------------
    // Fixtures
    // -----------------------------------------------------------------------

    private static readonly Guid OperatorId = Guid.Parse("aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa");
    private static readonly Guid OtherOperatorId = Guid.Parse("cccccccc-cccc-cccc-cccc-cccccccccccc");
    private static readonly Guid OperatorUserId = Guid.Parse("bbbbbbbb-bbbb-bbbb-bbbb-bbbbbbbbbbbb");

    private readonly IVoucherRepository _vouchers = Substitute.For<IVoucherRepository>();
    private readonly IClock _clock = Substitute.For<IClock>();

    private DeleteOperatorVoucherCommandHandler BuildSut() => new(
        _vouchers,
        _clock,
        NullLogger<DeleteOperatorVoucherCommandHandler>.Instance);

    private static Voucher CreateOwnerVoucher() =>
        Voucher.Create(
            code: "DELCODE1",
            name: "Delete Test Voucher",
            type: VoucherType.FIXED_AMOUNT,
            value: 10_000,
            minOrderAmount: Money.FromRaw(50_000),
            maxDiscountAmount: null,
            totalUsageLimit: 100,
            perUserLimit: 1,
            validFrom: DateTimeOffset.UtcNow.AddDays(-1),
            validUntil: DateTimeOffset.UtcNow.AddDays(30),
            applicableOperatorIds: [OperatorId],
            applicableRouteIds: null,
            fundingType: VoucherFundingType.OPERATOR_FUNDED,
            ownerOperatorId: OperatorId,
            createdByUserId: OperatorUserId);

    // -----------------------------------------------------------------------
    // Happy path — soft-delete sets deleted_at
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_ActiveVoucher_SoftDeletesAndCallsUpdate()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        _clock.UtcNow.Returns(now);

        var voucher = CreateOwnerVoucher();
        _vouchers.FindByIdAndOwnerIgnoringSoftDeleteAsync(voucher.Id, OperatorId, Arg.Any<CancellationToken>())
            .Returns(voucher);

        var sut = BuildSut();
        var command = new DeleteOperatorVoucherCommand(voucher.Id, OperatorId);

        // Act
        var result = await sut.Handle(command, CancellationToken.None);

        // Assert — result carries { id, deletedAt }
        result.Id.Should().Be(voucher.Id);
        result.DeletedAt.Should().Be(now);

        // deleted_at should be set on the entity
        voucher.DeletedAt.Should().Be(now);

        // Update must be called to persist the soft-delete
        _vouchers.Received(1).Update(voucher);
    }

    // -----------------------------------------------------------------------
    // Idempotency — already-soft-deleted voucher owned by caller → no-op (success)
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_AlreadySoftDeletedVoucher_ReturnsNoOpSuccess()
    {
        // Arrange
        var now = DateTimeOffset.UtcNow;
        _clock.UtcNow.Returns(now);

        var voucher = CreateOwnerVoucher();

        // Pre-delete the voucher via its own method to set DeletedAt
        voucher.SoftDelete(now.AddDays(-1));

        // The repository ignores the query filter and still finds the deleted voucher
        _vouchers.FindByIdAndOwnerIgnoringSoftDeleteAsync(voucher.Id, OperatorId, Arg.Any<CancellationToken>())
            .Returns(voucher);

        var sut = BuildSut();
        var command = new DeleteOperatorVoucherCommand(voucher.Id, OperatorId);

        // Act — must NOT throw; idempotent no-op
        var act = () => sut.Handle(command, CancellationToken.None);
        await act.Should().NotThrowAsync();

        // Update must NOT be called — nothing changed
        _vouchers.DidNotReceive().Update(Arg.Any<Voucher>());
    }

    // -----------------------------------------------------------------------
    // Tenant isolation — cross-operator access → 404 VOUCHER_NOT_FOUND
    // -----------------------------------------------------------------------

    [Fact]
    public async Task Handle_CrossOperatorAccess_ThrowsVoucherNotFound()
    {
        // Arrange — repository returns null for any voucher owned by a different operator
        var voucherId = Guid.NewGuid();
        _vouchers.FindByIdAndOwnerIgnoringSoftDeleteAsync(voucherId, OtherOperatorId, Arg.Any<CancellationToken>())
            .Returns((Voucher?)null);

        var sut = BuildSut();
        var command = new DeleteOperatorVoucherCommand(voucherId, OtherOperatorId);

        // Act
        var act = () => sut.Handle(command, CancellationToken.None);

        // Assert
        var ex = await act.Should().ThrowAsync<CodedNotFoundException>();
        ex.Which.ErrorCode.Should().Be("VOUCHER_NOT_FOUND");
    }
}
