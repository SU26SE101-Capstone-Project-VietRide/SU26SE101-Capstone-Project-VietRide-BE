using Hangfire;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using VietRide.Shared.Web.Authentication;
using VietRide.Shared.Web.Jobs;

namespace VietRide.Booking.Api.Controllers;

[ApiController]
[Authorize(AuthenticationSchemes = InternalJwtAuthenticationExtensions.Scheme)]
[Route("internal/jobs")]
public sealed class InternalJobsController : ControllerBase
{
    [HttpGet("status")]
    [ProducesResponseType(typeof(IReadOnlyList<InternalJobStatusDto>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<IReadOnlyList<InternalJobStatusDto>> GetStatus()
        => Ok(HangfireJobStatusReader.Read());
}

internal static class HangfireJobStatusReader
{
    public static IReadOnlyList<InternalJobStatusDto> Read()
    {
        var now = DateTimeOffset.UtcNow;
        using var connection = JobStorage.Current.GetConnection();
        return InternalJobStatusCollector.Collect(
            connection.GetAllItemsFromSet("recurring-jobs"),
            id => connection.GetAllEntriesFromHash($"recurring-job:{id}"),
            lastJobId => connection.GetStateData(lastJobId)?.Name,
            now);
    }
}
