using System.Text;
using FluentAssertions;
using VietRide.Identity.Application.Features.Admin.OutboxDlq;

namespace VietRide.Identity.UnitTests.Application.AdminOutboxDlq;

public sealed class GetAdminOutboxDlqQueryValidatorTests
{
    private readonly GetAdminOutboxDlqQueryValidator _validator = new();

    [Theory]
    [InlineData(0)]
    [InlineData(101)]
    public async Task RejectsPageSizeOutsideContract(int pageSize)
    {
        var result = await _validator.ValidateAsync(Query(pageSize: pageSize));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == nameof(GetAdminOutboxDlqQuery.PageSize));
    }

    [Fact]
    public async Task RejectsUnknownService()
    {
        var result = await _validator.ValidateAsync(Query(service: "notification"));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == nameof(GetAdminOutboxDlqQuery.Service));
    }

    [Fact]
    public async Task AcceptsCursorAndSupportedFilters()
    {
        var result = await _validator.ValidateAsync(Query(
            pageSize: 100,
            service: "Tracking",
            eventType: "tracking.location.updated",
            sortDir: "ASC",
            cursor: ValidCursor()));

        result.IsValid.Should().BeTrue();
    }

    private static string ValidCursor()
        => Convert.ToBase64String(Encoding.UTF8.GetBytes(
                "{\"Service\":\"tracking\",\"TerminalAt\":\"2026-07-18T00:00:00Z\",\"EventId\":\"41430000-0000-4000-8000-000000000001\"}"))
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');

    private static GetAdminOutboxDlqQuery Query(
        int pageSize = 50,
        string? service = null,
        string? eventType = null,
        string sortDir = "desc",
        string? cursor = null)
        => new(cursor, pageSize, service, eventType, sortDir);
}
