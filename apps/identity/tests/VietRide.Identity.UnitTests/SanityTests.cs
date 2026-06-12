using FluentAssertions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Identity.UnitTests;

/// Sanity check — verifies the test project references the right assemblies
/// (Identity.Domain + Identity.Application + Shared.Kernel). Real Identity-specific
/// tests (User registration, OAuth token rotation, Operator approval flow) land
/// Day 3+ alongside SCV-65.
///
/// Value-object correctness (Money to-the-đồng VND, PhoneNumber E.164 regex) is
/// covered ONCE in tests/dotnet/VietRide.Shared.Kernel.UnitTests — do NOT
/// duplicate those tests here.
public class SanityTests
{
    [Fact]
    public void IdentityUnitTestsProject_ReferencesSharedKernel()
    {
        Money.Zero.Amount.Should().Be(0);
    }
}
