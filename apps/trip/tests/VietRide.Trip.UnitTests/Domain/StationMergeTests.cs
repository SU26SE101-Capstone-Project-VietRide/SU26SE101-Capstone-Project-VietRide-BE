using FluentAssertions;
using VietRide.Shared.Kernel.ValueObjects;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.UnitTests.Domain;

public sealed class StationMergeTests
{
    [Fact]
    public void MergeProfile_PrimaryWinsAndOnlyMissingNullableFieldsAreFilled()
    {
        var primary = Station.Create(
            "Primary",
            "primary",
            "Primary City",
            "Primary Province",
            contactPhone: "0900000001",
            latitude: 10.1m,
            longitude: 106.1m);
        var duplicate = Station.Create(
            "Duplicate",
            "duplicate",
            "Duplicate City",
            "Duplicate Province",
            addressStreet: "12 Duplicate Street",
            latitude: 11.2m,
            longitude: 107.2m,
            contactPhone: "0900000002",
            contactEmail: "DUPLICATE@EXAMPLE.COM",
            operatingHours: "{\"mon\":\"06:00-22:00\"}",
            facilities: "[\"parking\"]",
            supportsShuttle: true);

        primary.MergeProfileFrom(duplicate);

        primary.Name.Should().Be("Primary");
        primary.Slug.Should().Be("primary");
        primary.City.Should().Be("Primary City");
        primary.Ward.Should().Be("Primary Province");
        primary.AddressStreet.Should().Be("12 Duplicate Street");
        primary.ContactPhone.Should().Be("0900000001");
        primary.ContactEmail.Should().Be("duplicate@example.com");
        primary.Latitude.Should().Be(10.1m);
        primary.Longitude.Should().Be(106.1m);
        primary.SupportsShuttle.Should().BeTrue();
    }

    [Fact]
    public void MergeProfile_CopiesCoordinatesOnlyAsACompletePair()
    {
        var primary = Station.Create("Primary", "primary", "City", "Province");
        var completeDuplicate = Station.Create(
            "Complete",
            "complete",
            "City",
            "Province",
            latitude: 10.2m,
            longitude: 106.2m);

        primary.MergeProfileFrom(completeDuplicate);

        primary.Latitude.Should().Be(10.2m);
        primary.Longitude.Should().Be(106.2m);

        var otherPrimary = Station.Create("Other", "other", "City", "Province");
        var partialDuplicate = Station.Create(
            "Partial",
            "partial",
            "City",
            "Province",
            latitude: 11.3m);
        otherPrimary.MergeProfileFrom(partialDuplicate);
        otherPrimary.Latitude.Should().BeNull();
        otherPrimary.Longitude.Should().BeNull();
    }

    [Fact]
    public void RedirectAndFlatten_KeepSoftDeleteSeparateFromCanonicalTarget()
    {
        var primaryId = Guid.NewGuid();
        var intermediateId = Guid.NewGuid();
        var duplicate = Station.Create("Duplicate", "duplicate", "City", "Province");
        var mergedAt = DateTimeOffset.UtcNow;

        duplicate.MarkMergedInto(intermediateId, mergedAt);
        duplicate.FlattenMergeRedirect(primaryId);

        duplicate.MergedIntoStationId.Should().Be(primaryId);
        duplicate.DeletedAt.Should().Be(mergedAt);
        duplicate.IsActive.Should().BeFalse();
        var selfRedirect = () => duplicate.FlattenMergeRedirect(duplicate.Id);
        selfRedirect.Should().Throw<ArgumentException>();
    }

    [Fact]
    public void OperatorStationCollision_MergesConfigurationAndActiveState()
    {
        var operatorId = Guid.NewGuid();
        var primary = OperatorStation.Create(operatorId, Guid.NewGuid(), contactPhone: "0900000001");
        primary.Deactivate();
        var duplicate = OperatorStation.Create(
            operatorId,
            Guid.NewGuid(),
            displayNameOverride: "Duplicate Counter",
            counterLocation: "Gate 2",
            contactPhone: "0900000002",
            instructions: "Arrive early");

        primary.MergeConfigurationFrom(duplicate);

        primary.IsActive.Should().BeTrue();
        primary.DisplayNameOverride.Should().Be("Duplicate Counter");
        primary.CounterLocation.Should().Be("Gate 2");
        primary.ContactPhone.Should().Be("0900000001");
        primary.Instructions.Should().Be("Arrive early");
    }

    [Fact]
    public void AggregateRelinkPrimitives_PreserveRouteInvariant()
    {
        var duplicateId = Guid.NewGuid();
        var primaryId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var route = Route.Create(
            Guid.NewGuid(),
            "Route",
            duplicateId,
            otherId,
            Money.FromRaw(100_000),
            null,
            null);
        var alternative = AlternativeRoute.Create(route.Id, "Alternative", duplicateId, null, null);
        var shuttle = ShuttleTrip.Create(
            Guid.NewGuid(),
            Guid.NewGuid(),
            duplicateId,
            Guid.NewGuid(),
            Guid.NewGuid(),
            DateTimeOffset.UtcNow,
            DateTimeOffset.UtcNow.AddHours(1),
            null);

        route.RelinkStation(duplicateId, primaryId).Should().Be((true, false));
        alternative.RelinkDestinationStation(duplicateId, primaryId).Should().BeTrue();
        shuttle.RelinkStation(duplicateId, primaryId).Should().BeTrue();
        route.OriginStationId.Should().Be(primaryId);
        alternative.DestinationStationId.Should().Be(primaryId);
        shuttle.StationId.Should().Be(primaryId);

        var conflicting = Route.Create(
            Guid.NewGuid(),
            "Conflict",
            duplicateId,
            primaryId,
            Money.FromRaw(100_000),
            null,
            null);
        var act = () => conflicting.RelinkStation(duplicateId, primaryId);
        act.Should().Throw<ArgumentException>();
    }
}
