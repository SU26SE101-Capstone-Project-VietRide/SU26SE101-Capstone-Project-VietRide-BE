using FluentAssertions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Payment.UnitTests;

/// Sanity check — verifies the test project references the right assemblies
/// (Payment.Domain + Payment.Application + Shared.Kernel). Real Payment-specific
/// tests (Wallet, VNPay IPN signature, refund ledger) start landing Day 15+.
///
/// Value-object correctness (Money to-the-đồng VND, PhoneNumber E.164 regex) is
/// covered ONCE in tests/dotnet/VietRide.Shared.Kernel.UnitTests — do NOT
/// duplicate those tests here.
public class SanityTests
{
    [Fact]
    public void PaymentUnitTestsProject_ReferencesSharedKernel()
    {
        Money.Zero.Amount.Should().Be(0);
    }
}
