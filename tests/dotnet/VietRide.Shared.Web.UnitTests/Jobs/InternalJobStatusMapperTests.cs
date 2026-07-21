using FluentAssertions;
using VietRide.Shared.Web.Jobs;
using Xunit;

namespace VietRide.Shared.Web.UnitTests.Jobs;

public sealed class InternalJobStatusMapperTests
{
    private static readonly DateTimeOffset Now =
        new(2026, 7, 18, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Map_ScheduledFutureJob_HasZeroLagAndUtcTimestamps()
    {
        var metadata = new Dictionary<string, string>
        {
            ["LastExecution"] = "2026-07-18T10:00:00.0000000Z",
            ["NextExecution"] = "2026-07-18T13:00:00.0000000Z",
        };

        var result = InternalJobStatusMapper.Map("payment.settlement", metadata, Now);

        result.Status.Should().Be("SCHEDULED");
        result.LastRun.Should().Be(new DateTimeOffset(2026, 7, 18, 10, 0, 0, TimeSpan.Zero));
        result.NextRun.Should().Be(new DateTimeOffset(2026, 7, 18, 13, 0, 0, TimeSpan.Zero));
        result.LagSeconds.Should().Be(0);
    }

    [Fact]
    public void Map_OverdueJob_CalculatesPositiveLag()
    {
        var metadata = new Dictionary<string, string>
        {
            ["NextExecution"] = "2026-07-18T11:58:30Z",
        };

        var result = InternalJobStatusMapper.Map("booking.expiry", metadata, Now);

        result.Status.Should().Be("SCHEDULED");
        result.LagSeconds.Should().Be(90);
    }

    [Fact]
    public void Map_ErrorMetadata_TakesFailedPrecedence()
    {
        var metadata = new Dictionary<string, string>
        {
            ["NextExecution"] = "2026-07-18T13:00:00Z",
            ["Error"] = "last execution failed",
        };

        var result = InternalJobStatusMapper.Map("parcel.timeout", metadata, Now);

        result.Status.Should().Be("FAILED");
        result.LagSeconds.Should().Be(0);
    }

    [Fact]
    public void Map_FailedLastJobState_TakesFailedPrecedence()
    {
        var metadata = new Dictionary<string, string>
        {
            ["NextExecution"] = "2026-07-18T13:00:00Z",
            ["LastJobState"] = "Failed",
        };

        var result = InternalJobStatusMapper.Map("trip.generator", metadata, Now);

        result.Status.Should().Be("FAILED");
        result.LagSeconds.Should().Be(0);
    }

    [Fact]
    public void Map_MissingOrMalformedDates_ReturnsDisabledWithoutThrowing()
    {
        var metadata = new Dictionary<string, string>
        {
            ["LastExecution"] = "not-a-date",
            ["NextExecution"] = "also-not-a-date",
        };

        var result = InternalJobStatusMapper.Map("identity.backfill", metadata, Now);

        result.Status.Should().Be("DISABLED");
        result.LastRun.Should().BeNull();
        result.NextRun.Should().BeNull();
        result.LagSeconds.Should().BeNull();
    }

    [Fact]
    public void Collect_ReturnsOneOrderedRowPerRegisteredJobAndResolvesLastState()
    {
        var metadata = new Dictionary<string, IReadOnlyDictionary<string, string>>
        {
            ["trip.z-job"] = new Dictionary<string, string>
            {
                ["LastJobId"] = "job-42",
                ["NextExecution"] = "2026-07-18T13:00:00Z",
            },
            ["trip.a-job"] = new Dictionary<string, string>(),
        };
        var requestedStates = new List<string>();

        var result = InternalJobStatusCollector.Collect(
            ["trip.z-job", "trip.a-job"],
            jobId => metadata[jobId],
            lastJobId =>
            {
                requestedStates.Add(lastJobId);
                return "Failed";
            },
            Now);

        result.Select(job => job.JobId).Should().Equal("trip.a-job", "trip.z-job");
        result[0].Status.Should().Be("DISABLED");
        result[1].Status.Should().Be("FAILED");
        requestedStates.Should().Equal("job-42");
    }
}
