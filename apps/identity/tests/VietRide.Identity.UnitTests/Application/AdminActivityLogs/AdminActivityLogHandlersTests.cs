using System.Reflection;
using FluentAssertions;
using NSubstitute;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Features.Admin.ListActivityLogs;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Identity.UnitTests.Application.AdminActivityLogs;

public sealed class AdminActivityLogHandlersTests
{
    [Fact]
    public async Task List_MapsActorAndJsonMetadataWithoutSecretUserFields()
    {
        var actor = User.CreateAdminPendingPassword("admin@example.com", "System Admin");
        actor.SetInitialPassword("hash");
        var activityLog = ActivityLog.Create(
            actor.Id,
            ActivityLogAction.LOCK_USER,
            "{\"targetUserId\":\"11111111-1111-1111-1111-111111111111\",\"statusChanged\":true}",
            "127.0.0.1",
            "tests");
        SetActor(activityLog, actor);
        var repository = Substitute.For<IActivityLogRepository>();
        repository.ListAsync(
                Arg.Any<QueryOptions>(),
                actor.Id,
                ActivityLogAction.LOCK_USER,
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<DateTimeOffset?>(),
                Arg.Any<CancellationToken>())
            .Returns(PagedResult<ActivityLog>.Create([activityLog], 1, 20, 1));
        var handler = new ListActivityLogsQueryHandler(repository);

        var result = await handler.Handle(
            new ListActivityLogsQuery(
                UserRole.SYSTEM_ADMIN.ToString(),
                actor.Id,
                ActivityLogAction.LOCK_USER.ToString(),
                DateTimeOffset.UtcNow.AddDays(-1),
                DateTimeOffset.UtcNow.AddDays(1),
                1,
                20),
            CancellationToken.None);

        var item = result.Items.Should().ContainSingle().Subject;
        item.Actor.Should().Be(new AdminActivityLogActorDto(
            actor.Id,
            actor.Email,
            actor.DisplayName,
            actor.Role.ToString()));
        item.Metadata.Should().NotBeNull();
        item.Metadata!.Value.GetProperty("statusChanged").GetBoolean().Should().BeTrue();
        typeof(AdminActivityLogActorDto).GetProperties().Select(property => property.Name)
            .Should().BeEquivalentTo(["Id", "Email", "DisplayName", "Role"]);
    }

    [Fact]
    public async Task List_NonSystemAdmin_IsForbidden()
    {
        var handler = new ListActivityLogsQueryHandler(Substitute.For<IActivityLogRepository>());

        var act = () => handler.Handle(
            new ListActivityLogsQuery(UserRole.PASSENGER.ToString(), null, null, null, null, 1, 20),
            CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>()
            .Where(exception => exception.ErrorCode == "FORBIDDEN");
    }

    [Fact]
    public void Validator_RejectsNonUtcInvalidRangeAndUnknownAction()
    {
        var validator = new ListActivityLogsQueryValidator();
        var result = validator.Validate(new ListActivityLogsQuery(
            UserRole.SYSTEM_ADMIN.ToString(),
            null,
            "UNKNOWN_ACTION",
            new DateTimeOffset(2026, 7, 16, 8, 0, 0, TimeSpan.FromHours(7)),
            new DateTimeOffset(2026, 7, 16, 0, 0, 0, TimeSpan.Zero),
            1,
            20));

        result.IsValid.Should().BeFalse();
        result.Errors.Should().Contain(error => error.PropertyName == "Action");
        result.Errors.Should().Contain(error => error.PropertyName == "From");
        result.Errors.Should().Contain(error => error.ErrorMessage == "From must be earlier than To.");
    }

    [Fact]
    public void RepositoryContract_ExposesOnlyAddAndReadOperations()
    {
        typeof(IActivityLogRepository).GetMethods().Select(method => method.Name)
            .Should().BeEquivalentTo(
                ["GetByIdAsync", "AddAsync", "ExistsBySourceEventIdAsync", "ListAsync"]);
    }

    private static void SetActor(ActivityLog activityLog, User actor)
    {
        var property = typeof(ActivityLog).GetProperty(
            nameof(ActivityLog.Actor),
            BindingFlags.Instance | BindingFlags.Public);
        property.Should().NotBeNull();
        property!.SetValue(activityLog, actor);
    }
}
