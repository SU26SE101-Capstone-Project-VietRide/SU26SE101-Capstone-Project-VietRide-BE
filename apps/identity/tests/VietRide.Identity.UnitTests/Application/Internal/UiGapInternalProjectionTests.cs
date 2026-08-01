using FluentAssertions;
using NSubstitute;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Features.Internal.Operators.GetOperatorSummaries;
using VietRide.Identity.Application.Features.InternalUsers.GetInternalUser;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Identity.UnitTests.Application.Internal;

public sealed class UiGapInternalProjectionTests
{
    [Fact]
    public async Task UserBatch_RedactsSoftDeletedUserAndPreservesActiveContactFields()
    {
        var active = User.CreateOperatorScopedPendingPassword(
            "active@example.com",
            PhoneNumber.Parse("+84901234567"),
            "Active User",
            UserRole.DRIVER,
            Guid.NewGuid());
        var deleted = User.CreateOperatorScopedPendingPassword(
            "deleted@example.com",
            PhoneNumber.Parse("+84907654321"),
            "Deleted User",
            UserRole.ASSISTANT,
            Guid.NewGuid());
        deleted.SoftDelete(DateTimeOffset.UtcNow);
        var repository = Substitute.For<IUserRepository>();
        repository.ListByIdsIncludingDeletedAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns([deleted, active]);
        var handler = new GetInternalUsersQueryHandler(repository);

        var result = await handler.Handle(
            new GetInternalUsersQuery([active.Id, deleted.Id]),
            CancellationToken.None);

        result.Select(item => item.Id).Should().Equal(active.Id, deleted.Id);
        var activeDto = result[0];
        activeDto.DisplayName.Should().Be("Active User");
        activeDto.Phone.Should().Be("+84901234567");
        activeDto.Email.Should().Be("active@example.com");
        activeDto.Deleted.Should().BeFalse();
        var deletedDto = result[1];
        deletedDto.DisplayName.Should().Be("Người dùng đã xóa");
        deletedDto.Phone.Should().BeNull();
        deletedDto.Email.Should().BeNull();
        deletedDto.AvatarUrl.Should().BeNull();
        deletedDto.Deleted.Should().BeTrue();
        await repository.Received(1).ListByIdsIncludingDeletedAsync(
            Arg.Is<IReadOnlyCollection<Guid>>(ids => ids.Count == 2),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task OperatorBatch_PreservesNameAndAddsLogoAndContactPhone()
    {
        var operatorTenant = Operator.CreatePending(
            "Nha xe A",
            "BR-001",
            "TAX-001",
            "ops@example.com",
            "+84901111111");
        operatorTenant.UpdateProfile(
            operatorTenant.Name,
            operatorTenant.ContactEmail,
            operatorTenant.ContactPhone,
            "https://example.test/logo.jpg",
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null,
            null);
        var repository = Substitute.For<IOperatorRepository>();
        repository.ListSummariesByIdsAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns([operatorTenant]);
        var handler = new GetOperatorSummariesQueryHandler(repository);

        var result = await handler.Handle(
            new GetOperatorSummariesQuery([operatorTenant.Id]),
            CancellationToken.None);

        result.Should().ContainSingle().Which.Should().Be(
            new InternalOperatorSummaryDto(
                operatorTenant.Id,
                "Nha xe A",
                "https://example.test/logo.jpg",
                "+84901111111"));
    }
}
