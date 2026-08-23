using FluentAssertions;
using VietRide.Payment.Domain.Entities;
using VietRide.Payment.Domain.Enums;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Payment.UnitTests.Domain;

public sealed class FinancialBusinessCodeTests
{
    private static readonly DateTimeOffset Instant = new(2026, 8, 23, 2, 0, 0, TimeSpan.Zero);

    [Fact]
    public void OperatorWalletTransaction_Create_GeneratesOwtCode()
    {
        var transaction = OperatorWalletTransaction.Create(
            Guid.NewGuid(),
            OperatorWalletTransactionType.CREDIT,
            Money.FromRaw(100_000),
            Money.Zero,
            Money.FromRaw(100_000),
            OperatorWalletTransactionRef.ADJUSTMENT,
            null,
            businessInstant: Instant);

        transaction.TransactionCode.Should().MatchRegex("^OWT-20260823-[0-9ABCDEFGHJKMNPQRSTVWXYZ]{8}$");
    }

    [Fact]
    public void PlatformWalletTransaction_Create_GeneratesPwtCode()
    {
        var transaction = PlatformWalletTransaction.Create(
            PlatformWalletTransactionType.CREDIT,
            Money.FromRaw(100_000),
            Money.Zero,
            Money.FromRaw(100_000),
            PlatformWalletTransactionRef.MANUAL_ADJUSTMENT,
            businessInstant: Instant);

        transaction.TransactionCode.Should().MatchRegex("^PWT-20260823-[0-9ABCDEFGHJKMNPQRSTVWXYZ]{8}$");
    }
}
