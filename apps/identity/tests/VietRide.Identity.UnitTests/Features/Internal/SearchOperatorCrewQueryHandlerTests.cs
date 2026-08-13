using FluentAssertions;
using NSubstitute;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Features.Internal.Operators.SearchOperatorCrew;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Identity.UnitTests.Features.Internal;

public sealed class SearchOperatorCrewQueryHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsMatchingCrewWithinOperatorRegardlessOfActivationStatus()
    {
        var operatorId = Guid.NewGuid();
        var otherOperatorId = Guid.NewGuid();
        var matchingDriver = CreateActiveUser(operatorId, UserRole.DRIVER, "Nguyen Van An", "+84901000001");
        var matchingAssistant = CreateActiveUser(operatorId, UserRole.ASSISTANT, "Tran An", "+84901000002");
        var staff = CreateActiveUser(operatorId, UserRole.OPERATOR_STAFF, "Staff An", "+84901000003");
        var otherTenant = CreateActiveUser(otherOperatorId, UserRole.DRIVER, "Other An", "+84901000004");
        var pendingCrew = User.CreateOperatorScopedPendingPassword(
            "pending@example.com",
            PhoneNumber.Parse("+84901000005"),
            "Pending An",
            UserRole.DRIVER,
            operatorId);
        var users = Substitute.For<IUserRepository>();
        users.QueryNoTracking().Returns(new[]
        {
            matchingDriver, matchingAssistant, staff, otherTenant, pendingCrew,
        }.AsQueryable());

        var result = await new SearchOperatorCrewQueryHandler(users).Handle(
            new SearchOperatorCrewQuery(operatorId, "an"),
            CancellationToken.None);

        result.Select(item => item.UserId).Should().BeEquivalentTo(
            [matchingDriver.Id, matchingAssistant.Id, pendingCrew.Id]);
        result.Select(item => item.Role).Should().BeEquivalentTo(["DRIVER", "ASSISTANT", "DRIVER"]);
    }

    private static User CreateActiveUser(
        Guid operatorId,
        UserRole role,
        string displayName,
        string phone)
    {
        var user = User.CreateOperatorScopedPendingPassword(
            $"{Guid.NewGuid():N}@example.com",
            PhoneNumber.Parse(phone),
            displayName,
            role,
            operatorId);
        user.SetInitialPassword("hash");
        return user;
    }
}
