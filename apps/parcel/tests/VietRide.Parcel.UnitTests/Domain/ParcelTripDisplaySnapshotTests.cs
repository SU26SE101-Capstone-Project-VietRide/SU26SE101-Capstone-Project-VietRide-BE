using FluentAssertions;
using VietRide.Parcel.Domain.Entities;
using VietRide.Parcel.Domain.Enums;
using VietRide.Shared.Kernel.ValueObjects;
using ParcelEntity = VietRide.Parcel.Domain.Entities.Parcel;

namespace VietRide.Parcel.UnitTests.Domain;

public sealed class ParcelTripDisplaySnapshotTests
{
    [Fact]
    public void CaptureTripDisplaySnapshot_NormalizesAndFreezesValues()
    {
        var parcel = CreateParcel();
        var routeId = Guid.NewGuid();
        var vehicleId = Guid.NewGuid();

        parcel.CaptureTripDisplaySnapshot(
            routeId,
            "  HCM - Da Lat  ",
            "  Mien Dong  ",
            "  Da Lat  ",
            vehicleId,
            "  51B-12345  ");

        parcel.TripSnapshotRouteId.Should().Be(routeId);
        parcel.TripSnapshotRouteName.Should().Be("HCM - Da Lat");
        parcel.TripSnapshotOriginStationName.Should().Be("Mien Dong");
        parcel.TripSnapshotDestinationStationName.Should().Be("Da Lat");
        parcel.TripSnapshotVehicleId.Should().Be(vehicleId);
        parcel.TripSnapshotVehicleLicensePlate.Should().Be("51B-12345");

        var overwrite = () => parcel.CaptureTripDisplaySnapshot(
            Guid.NewGuid(),
            "Different route",
            "Different origin",
            "Different destination",
            Guid.NewGuid(),
            "99A-99999");
        overwrite.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void NewLegacyCompatibleParcel_HasNullableSnapshots()
    {
        var parcel = CreateParcel();

        parcel.TripSnapshotRouteId.Should().BeNull();
        parcel.TripSnapshotRouteName.Should().BeNull();
        parcel.TripSnapshotOriginStationName.Should().BeNull();
        parcel.TripSnapshotDestinationStationName.Should().BeNull();
        parcel.TripSnapshotVehicleId.Should().BeNull();
        parcel.TripSnapshotVehicleLicensePlate.Should().BeNull();
    }

    private static ParcelEntity CreateParcel()
        => ParcelEntity.CreatePendingPayment(
            "VRP-20260730-TEST0001",
            Guid.NewGuid(),
            null,
            "Recipient",
            PhoneNumber.Normalize("+84912345678"),
            null,
            Guid.NewGuid(),
            Guid.NewGuid(),
            null,
            null,
            null,
            null,
            ParcelSizeCategory.MEDIUM,
            1m,
            ParcelDeliveryMethod.TERMINAL_PICKUP,
            Money.FromRaw(50_000));
}
