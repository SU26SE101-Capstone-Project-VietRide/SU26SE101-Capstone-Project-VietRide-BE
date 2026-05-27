using FluentAssertions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Booking.UnitTests;

/// Sanity check — verifies the test project references the right assemblies
/// (Booking.Domain + Booking.Application + Shared.Kernel). Real Booking-specific
/// tests (seat lock TTL, BookingPendingAction, voucher application) land Day 12+
/// alongside SCV-82.
///
/// Value-object correctness (Money floor 1000 VND, PhoneNumber E.164 regex) is
/// covered ONCE in tests/dotnet/VietRide.Shared.Kernel.UnitTests — do NOT
/// duplicate those tests here.
public class SanityTests
{
    [Fact]
    public void BookingUnitTestsProject_ReferencesSharedKernel()
    {
        Money.Zero.Amount.Should().Be(0);
    }
}
