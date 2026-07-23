using FluentAssertions;

namespace VietRide.Booking.IntegrationTests;

public sealed class StationWritingArchitectureTests
{
    private static readonly string[] StationWriteMarkers =
    [
        "BookingEntity.CreatePendingPayment(",
        ".ChangePickup(",
        ".ChangeDropoff(",
    ];

    private static readonly string[] ExpectedStationWriters =
    [
        "Features/Bookings/CreateBooking/CreateBookingCommandHandler.cs",
        "Features/Bookings/CreateRoundTripBooking/CreateRoundTripBookingCommandHandler.cs",
        "Features/Bookings/AcceptStopDisabledFallback/AcceptStopDisabledFallbackCommandHandler.cs",
        "Features/Bookings/EditDropoff/EditDropoffCommandHandler.cs",
        "Features/Bookings/EditPickup/EditPickupCommandHandler.cs",
        "Features/Bookings/ResolvePendingAction/ResolvePendingActionCommandHandler.cs",
    ];

    [Fact]
    public void EveryApplicationStationWriter_UsesTheSharedCanonicalizerAfterTripSnapshotFetch()
    {
        var applicationDirectory = FindApplicationDirectory();
        var writers = Directory.EnumerateFiles(applicationDirectory, "*Handler.cs", SearchOption.AllDirectories)
            .Select(path => new SourceFile(path, File.ReadAllText(path)))
            .Where(file => StationWriteMarkers.Any(marker => file.Content.Contains(marker, StringComparison.Ordinal)))
            .ToArray();

        writers.Select(file => NormalizeRelativePath(applicationDirectory, file.Path))
            .Should().BeEquivalentTo(ExpectedStationWriters);

        foreach (var writer in writers)
        {
            var directSnapshotIndex = writer.Content.IndexOf("GetTripSnapshotAsync", StringComparison.Ordinal);
            var scheduledSnapshotIndex = writer.Content.IndexOf("GetScheduledTripAsync", StringComparison.Ordinal);
            var snapshotIndex = new[] { directSnapshotIndex, scheduledSnapshotIndex }
                .Where(index => index >= 0)
                .DefaultIfEmpty(-1)
                .Min();
            var canonicalizerIndex = new[]
                {
                    writer.Content.IndexOf("_stationCanonicalizer.LockAndResolveAsync", StringComparison.Ordinal),
                    writer.Content.IndexOf("stationCanonicalizer.LockAndResolveAsync", StringComparison.Ordinal),
                }
                .Where(index => index >= 0)
                .DefaultIfEmpty(-1)
                .Min();

            canonicalizerIndex.Should().BeGreaterThanOrEqualTo(0, because: writer.Path);
            if (snapshotIndex >= 0)
            {
                canonicalizerIndex.Should().BeGreaterThan(snapshotIndex, because: writer.Path);
                writer.Content.Should().Contain("BookingStationCanonicalization.ResolveTrip", because: writer.Path);
            }
        }
    }

    private static string FindApplicationDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "apps",
                "booking",
                "src",
                "VietRide.Booking.Application");
            if (Directory.Exists(candidate))
                return candidate;

            directory = directory.Parent;
        }

        throw new DirectoryNotFoundException("Could not locate the Booking Application source directory.");
    }

    private static string NormalizeRelativePath(string root, string path)
        => Path.GetRelativePath(root, path).Replace('\\', '/');

    private sealed record SourceFile(string Path, string Content);
}
