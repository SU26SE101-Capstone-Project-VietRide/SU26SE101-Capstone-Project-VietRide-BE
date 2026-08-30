using FluentAssertions;
using VietRide.Shared.Application.Exceptions;
using VietRide.Trip.Application.Abstractions.ExternalClients;
using VietRide.Trip.Application.Features.Trips.Operations;

namespace VietRide.Trip.UnitTests.Features.Trips.Operations;

public sealed class VehicleSubstitutionSeatAssignmentPolicyTests
{
    private static readonly Guid BookingId = Guid.Parse("11111111-1111-4111-8111-111111111111");
    private static readonly Guid PassengerA1 = Guid.Parse("22222222-2222-4222-8222-222222222222");
    private static readonly Guid PassengerA2 = Guid.Parse("33333333-3333-4333-8333-333333333333");

    [Fact]
    public void Resolve_WhenBothOriginalSeatsExist_PreservesA1AndA2WithoutPreview()
    {
        var result = VehicleSubstitutionSeatAssignmentPolicy.Resolve(
            Impact((PassengerA1, "A1"), (PassengerA2, "A2")),
            ["A1", "A2", "A10"],
            null,
            null,
            "TOKEN");

        result.Should().BeEquivalentTo(new Dictionary<Guid, string>
        {
            [PassengerA1] = "A1",
            [PassengerA2] = "A2",
        });
    }

    [Fact]
    public void CreatePreview_WhenA2IsMissing_ReservesA1AndRequiresAdminSelectionFromRemainingSeats()
    {
        var result = VehicleSubstitutionSeatAssignmentPolicy.CreatePreview(
            Impact((PassengerA1, "A1"), (PassengerA2, "A2")),
            ["A1", "A5", "A10"]);

        result.Single(item => item.PassengerId == PassengerA1).Should().BeEquivalentTo(
            new SubstituteVehicleSeatPreview(BookingId, PassengerA1, "A1", "A1", false, []));
        result.Single(item => item.PassengerId == PassengerA2).Should().BeEquivalentTo(
            new SubstituteVehicleSeatPreview(BookingId, PassengerA2, "A2", null, true, ["A10", "A5"]),
            options => options.WithStrictOrdering());
    }

    [Fact]
    public void Resolve_WhenA2IsMissing_RequiresPreviewAndAcceptsAdminSelectedA5()
    {
        var impact = Impact((PassengerA1, "A1"), (PassengerA2, "A2"));
        var missing = () => VehicleSubstitutionSeatAssignmentPolicy.Resolve(
            impact,
            ["A1", "A5"],
            null,
            null,
            "TOKEN");
        missing.Should().Throw<CodedConflictException>()
            .Where(exception => exception.ErrorCode == "REPLACEMENT_SEAT_ASSIGNMENT_REQUIRED");

        var result = VehicleSubstitutionSeatAssignmentPolicy.Resolve(
            impact,
            ["A1", "A5"],
            [new SubstituteVehicleSeatAssignment(PassengerA2, "a5")],
            "TOKEN",
            "TOKEN");

        result[PassengerA1].Should().Be("A1");
        result[PassengerA2].Should().Be("A5");
    }

    [Fact]
    public void Resolve_RejectsStalePreviewUnavailableSeatAndDuplicateAssignments()
    {
        var impact = Impact((PassengerA1, "A1"), (PassengerA2, "A2"));
        var stale = () => VehicleSubstitutionSeatAssignmentPolicy.Resolve(
            impact,
            ["A1", "A5"],
            [new SubstituteVehicleSeatAssignment(PassengerA2, "A5")],
            "OLD",
            "NEW");
        stale.Should().Throw<CodedConflictException>()
            .Where(exception => exception.ErrorCode == "REPLACEMENT_SEAT_PREVIEW_STALE");

        var unavailable = () => VehicleSubstitutionSeatAssignmentPolicy.Resolve(
            impact,
            ["A1", "A5"],
            [new SubstituteVehicleSeatAssignment(PassengerA2, "A9")],
            "TOKEN",
            "TOKEN");
        unavailable.Should().Throw<CodedConflictException>()
            .Where(exception => exception.ErrorCode == "REPLACEMENT_SEAT_NOT_AVAILABLE");

        var duplicate = () => VehicleSubstitutionSeatAssignmentPolicy.Resolve(
            impact,
            ["A1", "A5"],
            [
                new SubstituteVehicleSeatAssignment(PassengerA2, "A5"),
                new SubstituteVehicleSeatAssignment(PassengerA2, "A10"),
            ],
            "TOKEN",
            "TOKEN");
        duplicate.Should().Throw<CodedConflictException>()
            .Where(exception => exception.ErrorCode == "REPLACEMENT_SEAT_NOT_AVAILABLE");
    }

    private static VehicleSubstitutionImpactProjection Impact(
        params (Guid PassengerId, string? SeatNumber)[] passengers)
        => new(
            Guid.Parse("44444444-4444-4444-8444-444444444444"),
            Guid.Parse("55555555-5555-4555-8555-555555555555"),
            [new VehicleSubstitutionImpactProjection.Booking(
                BookingId,
                "CONFIRMED",
                passengers.Select(item => new VehicleSubstitutionImpactProjection.Passenger(
                    item.PassengerId,
                    "PENDING",
                    item.SeatNumber)).ToArray())]);
}
