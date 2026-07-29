using FluentAssertions;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;
using VietRide.Payment.Domain.ValueObjects;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Payment.UnitTests.Domain;

public sealed class FinancialProjectionSnapshotTests
{
    private static readonly DateTimeOffset TerminalAt = new(2026, 7, 1, 0, 0, 0, TimeSpan.Zero);

    [Fact]
    public void PlatformTransaction_AutomatedWrite_IsSystemActor()
    {
        var transaction = PlatformWalletTransaction.Create(
            PlatformWalletTransactionType.CREDIT,
            Money.FromRaw(100_000),
            Money.FromRaw(0),
            Money.FromRaw(100_000),
            PlatformWalletTransactionRef.SUBSCRIPTION_PAYMENT,
            Guid.NewGuid());

        transaction.ActorType.Should().Be(FinancialActorType.SYSTEM);
        transaction.ActorUserId.Should().BeNull();
        transaction.ActorDisplayName.Should().BeNull();
        transaction.ActorEmail.Should().BeNull();
        transaction.ActorRole.Should().BeNull();
        transaction.ActorSnapshotResolved.Should().BeTrue();
    }

    [Fact]
    public void PlatformTransaction_ManualWrite_PersistsCompleteUserSnapshot()
    {
        var actor = new FinancialActorSnapshot(
            Guid.NewGuid(),
            "System Admin",
            "admin@vietride.vn",
            "SYSTEM_ADMIN");
        var transaction = PlatformWalletTransaction.Create(
            PlatformWalletTransactionType.DEBIT,
            Money.FromRaw(50_000),
            Money.FromRaw(100_000),
            Money.FromRaw(50_000),
            PlatformWalletTransactionRef.MANUAL_ADJUSTMENT,
            note: "Manual correction");

        transaction.AssignUserActor(actor);

        transaction.ActorType.Should().Be(FinancialActorType.USER);
        transaction.ActorUserId.Should().Be(actor.UserId);
        transaction.ActorDisplayName.Should().Be(actor.DisplayName);
        transaction.ActorEmail.Should().Be(actor.Email);
        transaction.ActorRole.Should().Be(actor.Role);
        transaction.ActorSnapshotResolved.Should().BeTrue();
    }

    [Fact]
    public void ManualSettlement_PersistsOperatorAndSettledBySnapshots()
    {
        var operatorId = Guid.NewGuid();
        var operatorSnapshot = new FinancialOperatorSnapshot(
            operatorId,
            "VietRide Limousine",
            "https://example.test/logo.jpg",
            "+84901234567");
        var actor = new FinancialActorSnapshot(
            Guid.NewGuid(),
            "System Admin",
            "admin@vietride.vn",
            "SYSTEM_ADMIN");
        var settlement = OperatorTripSettlement.CreatePending(operatorId, Guid.NewGuid(), TerminalAt);
        settlement.RefreshEligibility(500_000, TerminalAt.AddDays(7));

        settlement.SetOperatorSnapshot(operatorSnapshot);
        settlement.MarkSettled(
            500_000,
            OperatorTripSettlementMethod.ADMIN_MANUAL,
            TerminalAt.AddDays(8),
            actor,
            Guid.NewGuid());

        settlement.OperatorSnapshotResolved.Should().BeTrue();
        settlement.OperatorName.Should().Be(operatorSnapshot.Name);
        settlement.OperatorLogoUrl.Should().Be(operatorSnapshot.LogoUrl);
        settlement.OperatorContactPhone.Should().Be(operatorSnapshot.ContactPhone);
        settlement.SettledBySnapshotResolved.Should().BeTrue();
        settlement.SettledByUserId.Should().Be(actor.UserId);
        settlement.SettledByDisplayName.Should().Be(actor.DisplayName);
        settlement.SettledByEmail.Should().Be(actor.Email);
        settlement.SettledByRole.Should().Be(actor.Role);
    }
}
