using System.Text.Json;
using FluentAssertions;
using VietRide.Booking.Application.Events;
using Xunit;

namespace VietRide.Booking.UnitTests.Features.Bookings;

public sealed class Day24StopDisabledActionTests
{
    [Fact]
    public void Day24_StopDisabledAffectedEvent_ContainsFrozenFields()
    {
        var stop = Guid.NewGuid();
        var user = Guid.NewGuid();
        var evt = new StopDisabledBookingAffectedIntegrationEvent(
            Guid.NewGuid(), DateTimeOffset.UtcNow, stop, null, [user], 1);
        using var json = JsonDocument.Parse(JsonSerializer.Serialize(evt, new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
        }));
        var root = json.RootElement;
        root.GetProperty("eventType").GetString().Should().Be("booking.stop_disabled.affected");
        root.GetProperty("stopId").GetGuid().Should().Be(stop);
        root.GetProperty("recipientUserIds")[0].GetGuid().Should().Be(user);
        root.GetProperty("affectedBookingCount").GetInt32().Should().Be(1);
        root.TryGetProperty("replacedByStopId", out _).Should().BeFalse();
    }
}
