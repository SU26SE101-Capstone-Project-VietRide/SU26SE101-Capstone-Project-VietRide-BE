using VietRide.Parcel.Application.Abstractions.Repositories;
using VietRide.Parcel.Domain.Enums;

namespace VietRide.Parcel.Application.Features.Reliability.Incidents;

internal static class ParcelIncidentSearchTaskLifecycle
{
    public static async Task CancelOutstandingAsync(
        IParcelReliabilityRepository reliability,
        Guid incidentId,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        var tasks = await reliability.ListSearchTasksAsync(incidentId, cancellationToken);
        foreach (var task in tasks.Where(IsOutstanding))
        {
            task.Cancel(at);
            await reliability.UpdateSearchTaskAsync(task, cancellationToken);
        }
    }

    public static async Task FailOutstandingAsync(
        IParcelReliabilityRepository reliability,
        Guid incidentId,
        string result,
        DateTimeOffset at,
        CancellationToken cancellationToken)
    {
        var tasks = await reliability.ListSearchTasksAsync(incidentId, cancellationToken);
        foreach (var task in tasks.Where(IsOutstanding))
        {
            task.Fail(result, evidenceJson: null, at);
            await reliability.UpdateSearchTaskAsync(task, cancellationToken);
        }
    }

    private static bool IsOutstanding(Domain.Entities.ParcelSearchTask task)
        => task.Status is ParcelSearchTaskStatus.OPEN or ParcelSearchTaskStatus.IN_PROGRESS;
}
