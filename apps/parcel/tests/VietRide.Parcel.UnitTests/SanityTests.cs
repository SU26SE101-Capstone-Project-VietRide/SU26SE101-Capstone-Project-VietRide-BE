using FluentAssertions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Parcel.UnitTests;

/// Sanity check — verifies the test project references the right assemblies
/// (Parcel.Domain + Parcel.Application + Shared.Kernel). Real Parcel-specific
/// tests (lifecycle transitions, capacity counter, deliveryToken) start landing
/// Day 25+.
///
/// Value-object correctness (Money to-the-đồng VND, PhoneNumber E.164 regex) is
/// covered ONCE in tests/dotnet/VietRide.Shared.Kernel.UnitTests — do NOT
/// duplicate those tests here.
public class SanityTests
{
    [Fact]
    public void ParcelUnitTestsProject_ReferencesSharedKernel()
    {
        Money.Zero.Amount.Should().Be(0);
    }
}
