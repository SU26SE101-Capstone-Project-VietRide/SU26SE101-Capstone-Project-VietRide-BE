using System.Net;
using System.Net.Http.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using VietRide.Shared.Kernel.Primitives;
using VietRide.Trip.Application.Features.Internal.Trips.Cargo;
using VietRide.Trip.Application.Features.Trips.Operations;
using VietRide.Trip.Domain.Entities;

namespace VietRide.Trip.IntegrationTests.Trips;

public sealed class VehicleSubstitutionCargoConservationTests
{
    [Fact]
    public async Task ReplacementStartsEmptyAndNoConfirmationKeepsAllCargoOnSource()
    {
        await using var harness =
            await SubstituteVehicleEndpointTests.SubstitutionHarness.CreateAsync();
        var parcelIds = await SeedLoadedCargoAsync(harness, (12.5m, 0.2m), (7.5m, 0.1m));

        var replacementTripId = await SubstituteAsync(harness);

        await AssertCargoAsync(
            harness,
            replacementTripId,
            sourceLoadedWeight: 20m,
            sourceLoadedVolume: 0.3m,
            targetLoadedWeight: 0m,
            targetLoadedVolume: 0m,
            parcelIds,
            expectedTargetParcels: []);
    }

    [Fact]
    public async Task SingleConfirmationMovesOneLedgerAndConservesCargoAcrossTrips()
    {
        await using var harness =
            await SubstituteVehicleEndpointTests.SubstitutionHarness.CreateAsync();
        var parcelIds = await SeedLoadedCargoAsync(harness, (12.5m, 0.2m));
        var replacementTripId = await SubstituteAsync(harness);

        using var response = await harness.SendCargoTransferAsync(
            harness.OldTripId,
            parcelIds[0],
            replacementTripId,
            Guid.NewGuid());

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        await AssertCargoAsync(
            harness,
            replacementTripId,
            sourceLoadedWeight: 0m,
            sourceLoadedVolume: 0m,
            targetLoadedWeight: 12.5m,
            targetLoadedVolume: 0.2m,
            parcelIds,
            expectedTargetParcels: parcelIds);
    }

    [Fact]
    public async Task MultipleConfirmationsConserveCargoAfterPartialAndCompleteTransfer()
    {
        await using var harness =
            await SubstituteVehicleEndpointTests.SubstitutionHarness.CreateAsync();
        var parcelIds = await SeedLoadedCargoAsync(harness, (12.5m, 0.2m), (7.5m, 0.1m));
        var replacementTripId = await SubstituteAsync(harness);

        using var firstResponse = await harness.SendCargoTransferAsync(
            harness.OldTripId,
            parcelIds[0],
            replacementTripId,
            Guid.NewGuid());
        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        await AssertCargoAsync(
            harness,
            replacementTripId,
            sourceLoadedWeight: 7.5m,
            sourceLoadedVolume: 0.1m,
            targetLoadedWeight: 12.5m,
            targetLoadedVolume: 0.2m,
            parcelIds,
            expectedTargetParcels: [parcelIds[0]]);

        using var secondResponse = await harness.SendCargoTransferAsync(
            harness.OldTripId,
            parcelIds[1],
            replacementTripId,
            Guid.NewGuid());
        secondResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        await AssertCargoAsync(
            harness,
            replacementTripId,
            sourceLoadedWeight: 0m,
            sourceLoadedVolume: 0m,
            targetLoadedWeight: 20m,
            targetLoadedVolume: 0.3m,
            parcelIds,
            expectedTargetParcels: parcelIds);
    }

    [Fact]
    public async Task SameKeyReplayReturnsOriginalSuccessWithoutMovingCargoTwice()
    {
        await using var harness =
            await SubstituteVehicleEndpointTests.SubstitutionHarness.CreateAsync();
        var parcelIds = await SeedLoadedCargoAsync(harness, (12.5m, 0.2m));
        var replacementTripId = await SubstituteAsync(harness);
        var idempotencyKey = Guid.NewGuid();

        using var firstResponse = await harness.SendCargoTransferAsync(
            harness.OldTripId,
            parcelIds[0],
            replacementTripId,
            idempotencyKey);
        using var replayResponse = await harness.SendCargoTransferAsync(
            harness.OldTripId,
            parcelIds[0],
            replacementTripId,
            idempotencyKey);

        firstResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        replayResponse.StatusCode.Should().Be(HttpStatusCode.OK);
        var first = await firstResponse.Content.ReadFromJsonAsync<ApiResponse<CargoTransferDto>>();
        var replay = await replayResponse.Content.ReadFromJsonAsync<ApiResponse<CargoTransferDto>>();
        replay!.Data.Should().BeEquivalentTo(first!.Data);
        await AssertCargoAsync(
            harness,
            replacementTripId,
            sourceLoadedWeight: 0m,
            sourceLoadedVolume: 0m,
            targetLoadedWeight: 12.5m,
            targetLoadedVolume: 0.2m,
            parcelIds,
            expectedTargetParcels: parcelIds);
    }

    private static async Task<Guid> SubstituteAsync(
        SubstituteVehicleEndpointTests.SubstitutionHarness harness)
    {
        using var response = await harness.SendAsync();
        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var body = await response.Content
            .ReadFromJsonAsync<ApiResponse<SubstituteVehicleResponse>>();
        return body!.Data!.NewTripId;
    }

    private static async Task<Guid[]> SeedLoadedCargoAsync(
        SubstituteVehicleEndpointTests.SubstitutionHarness harness,
        params (decimal WeightKg, decimal VolumeM3)[] cargo)
    {
        await using var db = harness.OpenDb();
        var source = await db.Trips.SingleAsync(trip => trip.Id == harness.OldTripId);
        var rows = cargo.Select(item =>
        {
            var parcel = TripCargoParcel.Reserve(
                harness.OldTripId,
                Guid.NewGuid(),
                item.WeightKg,
                item.VolumeM3);
            parcel.MarkLoaded(SubstituteVehicleEndpointTests.SubstitutionHarness.Now.AddHours(-1));
            return parcel;
        }).ToArray();
        source.UpdateCargoCounters(
            0m,
            0m,
            rows.Sum(row => row.WeightKg),
            rows.Sum(row => row.VolumeM3));
        db.TripCargoParcels.AddRange(rows);
        await db.SaveChangesAsync();
        return rows.Select(row => row.ParcelId).ToArray();
    }

    private static async Task AssertCargoAsync(
        SubstituteVehicleEndpointTests.SubstitutionHarness harness,
        Guid replacementTripId,
        decimal sourceLoadedWeight,
        decimal sourceLoadedVolume,
        decimal targetLoadedWeight,
        decimal targetLoadedVolume,
        IReadOnlyCollection<Guid> parcelIds,
        IReadOnlyCollection<Guid> expectedTargetParcels)
    {
        await using var db = harness.OpenDb();
        var trips = await db.Trips.AsNoTracking()
            .Where(trip => trip.Id == harness.OldTripId || trip.Id == replacementTripId)
            .ToDictionaryAsync(trip => trip.Id);
        trips[harness.OldTripId].ReservedParcelWeightKg.Should().Be(0m);
        trips[harness.OldTripId].ReservedParcelVolumeM3.Should().Be(0m);
        trips[harness.OldTripId].TotalLoadedWeightKg.Should().Be(sourceLoadedWeight);
        trips[harness.OldTripId].TotalLoadedVolumeM3.Should().Be(sourceLoadedVolume);
        trips[replacementTripId].ReservedParcelWeightKg.Should().Be(0m);
        trips[replacementTripId].ReservedParcelVolumeM3.Should().Be(0m);
        trips[replacementTripId].TotalLoadedWeightKg.Should().Be(targetLoadedWeight);
        trips[replacementTripId].TotalLoadedVolumeM3.Should().Be(targetLoadedVolume);
        (trips[harness.OldTripId].TotalLoadedWeightKg
            + trips[replacementTripId].TotalLoadedWeightKg)
            .Should().Be(sourceLoadedWeight + targetLoadedWeight);
        (trips[harness.OldTripId].TotalLoadedVolumeM3
            + trips[replacementTripId].TotalLoadedVolumeM3)
            .Should().Be(sourceLoadedVolume + targetLoadedVolume);

        var ledgers = await db.TripCargoParcels.AsNoTracking()
            .Where(row => parcelIds.Contains(row.ParcelId))
            .ToArrayAsync();
        ledgers.Should().HaveCount(parcelIds.Count + expectedTargetParcels.Count);
        foreach (var parcelId in parcelIds)
        {
            var sourceRows = ledgers.Where(row =>
                    row.ParcelId == parcelId
                    && row.TripId == harness.OldTripId)
                .ToArray();
            sourceRows.Should().ContainSingle();
            var targetRows = ledgers.Where(row =>
                    row.ParcelId == parcelId
                    && row.TripId == replacementTripId)
                .ToArray();
            if (expectedTargetParcels.Contains(parcelId))
            {
                sourceRows[0].State.Should().Be(TripCargoParcel.ReleasedState);
                sourceRows[0].ReleasedAt.Should().NotBeNull();
                targetRows.Should().ContainSingle();
                targetRows[0].State.Should().Be(TripCargoParcel.LoadedState);
                targetRows[0].LoadedAt.Should().NotBeNull();
                targetRows[0].ReleasedAt.Should().BeNull();
            }
            else
            {
                sourceRows[0].State.Should().Be(TripCargoParcel.LoadedState);
                sourceRows[0].ReleasedAt.Should().BeNull();
                targetRows.Should().BeEmpty();
            }
        }
    }
}
