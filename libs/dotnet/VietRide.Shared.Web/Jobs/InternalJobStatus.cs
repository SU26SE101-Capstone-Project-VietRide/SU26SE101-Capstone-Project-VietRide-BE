using System.Globalization;

namespace VietRide.Shared.Web.Jobs;

public sealed record InternalJobStatusDto(
    string JobId,
    string Status,
    DateTimeOffset? LastRun,
    DateTimeOffset? NextRun,
    long? LagSeconds);

public static class InternalJobStatusMapper
{
    public static InternalJobStatusDto Map(
        string jobId,
        IReadOnlyDictionary<string, string> metadata,
        DateTimeOffset nowUtc)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(jobId);
        ArgumentNullException.ThrowIfNull(metadata);

        var lastRun = ReadUtc(metadata, "LastExecution");
        var nextRun = ReadUtc(metadata, "NextExecution");
        var failed = metadata.TryGetValue("Error", out var error)
                && !string.IsNullOrWhiteSpace(error)
            || metadata.TryGetValue("LastJobState", out var lastJobState)
                && string.Equals(lastJobState, "Failed", StringComparison.OrdinalIgnoreCase);
        var status = failed ? "FAILED" : nextRun.HasValue ? "SCHEDULED" : "DISABLED";
        long? lagSeconds = nextRun.HasValue
            ? Math.Max(0, (long)(nowUtc.ToUniversalTime() - nextRun.Value).TotalSeconds)
            : null;

        return new InternalJobStatusDto(jobId, status, lastRun, nextRun, lagSeconds);
    }

    private static DateTimeOffset? ReadUtc(
        IReadOnlyDictionary<string, string> metadata,
        string key)
    {
        if (!metadata.TryGetValue(key, out var raw)
            || string.IsNullOrWhiteSpace(raw)
            || !DateTimeOffset.TryParse(
                raw,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal,
                out var parsed))
        {
            return null;
        }

        return parsed.ToUniversalTime();
    }
}

public static class InternalJobStatusCollector
{
    public static IReadOnlyList<InternalJobStatusDto> Collect(
        IEnumerable<string> jobIds,
        Func<string, IReadOnlyDictionary<string, string>> metadataReader,
        Func<string, string?> lastJobStateReader,
        DateTimeOffset nowUtc)
    {
        ArgumentNullException.ThrowIfNull(jobIds);
        ArgumentNullException.ThrowIfNull(metadataReader);
        ArgumentNullException.ThrowIfNull(lastJobStateReader);

        return jobIds
            .Select(jobId =>
            {
                var metadata = new Dictionary<string, string>(
                    metadataReader(jobId),
                    StringComparer.Ordinal);
                if (metadata.TryGetValue("LastJobId", out var lastJobId)
                    && !string.IsNullOrWhiteSpace(lastJobId))
                {
                    var state = lastJobStateReader(lastJobId);
                    if (!string.IsNullOrWhiteSpace(state))
                        metadata["LastJobState"] = state;
                }

                return InternalJobStatusMapper.Map(jobId, metadata, nowUtc);
            })
            .OrderBy(job => job.JobId, StringComparer.Ordinal)
            .ToArray();
    }
}
