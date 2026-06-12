using FluentAssertions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Trip.UnitTests;

/// Sanity check — verifies the test project references the right assemblies
/// (Trip.Domain + Trip.Application + Shared.Kernel). Real Trip-specific tests
/// (Route/Trip/Vehicle/DriverSchedule entity rules) start landing Day 7+.
///
/// Value-object correctness (Money to-the-đồng VND, PhoneNumber E.164 regex) is
/// covered ONCE in tests/dotnet/VietRide.Shared.Kernel.UnitTests — do NOT
/// duplicate those tests here.
public class SanityTests
{
    [Fact]
    public void TripUnitTestsProject_ReferencesSharedKernel()
    {
        Money.Zero.Amount.Should().Be(0);
    }
}
