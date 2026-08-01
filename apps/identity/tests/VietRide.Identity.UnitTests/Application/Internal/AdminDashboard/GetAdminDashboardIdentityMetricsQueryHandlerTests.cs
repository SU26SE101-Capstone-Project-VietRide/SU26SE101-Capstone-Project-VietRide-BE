using FluentAssertions;
using NSubstitute;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Features.Internal.AdminDashboard;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Identity.UnitTests.Application.Internal.AdminDashboard;

public sealed class GetAdminDashboardIdentityMetricsQueryHandlerTests
{
    private readonly IAdminDashboardIdentityMetricsRepository _repository =
        Substitute.For<IAdminDashboardIdentityMetricsRepository>();

    [Fact]
    public async Task Handle_UsesInclusiveIctRangeAndReturnsDeterministicRawMetrics()
    {
        var firstOperatorId = Guid.Parse("11111111-1111-4111-8111-111111111111");
        var secondOperatorId = Guid.Parse("22222222-2222-4222-8222-222222222222");
        _repository.GetAsync(
                DateTimeOffset.Parse("2026-01-31T17:00:00Z"),
                DateTimeOffset.Parse("2026-02-01T17:00:00Z"),
                Arg.Any<CancellationToken>())
            .Returns(new AdminDashboardIdentityMetricsReadResult(
                2,
                [secondOperatorId, firstOperatorId, firstOperatorId],
                [
                    new AdminDashboardIdentityMetricCountReadModel("DRIVER", 1),
                    new AdminDashboardIdentityMetricCountReadModel("PASSENGER", 5),
                ],
                [
                    new AdminDashboardIdentityMetricCountReadModel("SUSPENDED", 1),
                    new AdminDashboardIdentityMetricCountReadModel("APPROVED", 3),
                ]));
        var handler = new GetAdminDashboardIdentityMetricsQueryHandler(_repository);

        var result = await handler.Handle(
            new GetAdminDashboardIdentityMetricsQuery(
                new DateOnly(2026, 2, 1),
                new DateOnly(2026, 2, 1)),
            CancellationToken.None);

        result.ActiveUserCount.Should().Be(2);
        result.ApprovedActiveOperatorIds.Should().Equal(firstOperatorId, secondOperatorId);
        result.UserRoleCounts.Select(item => (item.Role, item.Count)).Should().Equal(
            ("PASSENGER", 5L),
            ("DRIVER", 1L));
        result.OperatorStatusCounts.Select(item => (item.Status, item.Count)).Should().Equal(
            ("APPROVED", 3L),
            ("SUSPENDED", 1L));
    }

    [Theory]
    [InlineData(null, "2026-01-31")]
    [InlineData("2026-01-01", null)]
    [InlineData("2026-02-01", "2026-01-31")]
    [InlineData("2025-01-01", "2026-01-02")]
    public async Task Handle_RejectsMissingReversedOrOversizedRangeBeforeRepository(
        string? fromValue,
        string? toValue)
    {
        var handler = new GetAdminDashboardIdentityMetricsQueryHandler(_repository);
        DateOnly? from = fromValue is null ? null : DateOnly.Parse(fromValue);
        DateOnly? to = toValue is null ? null : DateOnly.Parse(toValue);

        var act = () => handler.Handle(
            new GetAdminDashboardIdentityMetricsQuery(from, to),
            CancellationToken.None);

        var exception = await act.Should().ThrowAsync<CodedValidationException>();
        exception.Which.ErrorCode.Should().Be("VALIDATION_ERROR");
        await _repository.DidNotReceiveWithAnyArgs().GetAsync(default, default, default);
    }
}
