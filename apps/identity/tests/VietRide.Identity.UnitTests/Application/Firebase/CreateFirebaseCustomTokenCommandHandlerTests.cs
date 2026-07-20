using FluentAssertions;
using NSubstitute;
using VietRide.Identity.Application.Abstractions;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Features.Firebase.CreateFirebaseCustomToken;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Identity.UnitTests.Application.Firebase;

public sealed class CreateFirebaseCustomTokenCommandHandlerTests
{
    [Fact]
    public async Task ActiveAdminOfApprovedOperator_ReceivesTokenUsingPersistedScope()
    {
        var operatorEntity = ApprovedOperator();
        var user = ActiveOperatorAdmin(operatorEntity.Id);
        var users = Substitute.For<IUserRepository>();
        var operators = Substitute.For<IOperatorRepository>();
        var firebase = Substitute.For<IFirebaseAuthService>();
        users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        operators.GetByIdNoTrackingAsync(operatorEntity.Id, Arg.Any<CancellationToken>())
            .Returns(operatorEntity);
        firebase.CreateOperatorCustomTokenAsync(user.Id, operatorEntity.Id, Arg.Any<CancellationToken>())
            .Returns("custom-token");
        var handler = new CreateFirebaseCustomTokenCommandHandler(users, operators, firebase);

        var result = await handler.Handle(
            new CreateFirebaseCustomTokenCommand(
                user.Id,
                UserRole.OPERATOR_ADMIN.ToString(),
                operatorEntity.Id),
            CancellationToken.None);

        result.Token.Should().Be("custom-token");
        await firebase.Received(1).CreateOperatorCustomTokenAsync(
            user.Id,
            operatorEntity.Id,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task LockedUser_IsRejectedBeforeFirebaseCall()
    {
        var operatorEntity = ApprovedOperator();
        var user = ActiveOperatorAdmin(operatorEntity.Id);
        user.Lock();
        var users = Substitute.For<IUserRepository>();
        var operators = Substitute.For<IOperatorRepository>();
        var firebase = Substitute.For<IFirebaseAuthService>();
        users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        var handler = new CreateFirebaseCustomTokenCommandHandler(users, operators, firebase);

        var action = () => handler.Handle(
            new CreateFirebaseCustomTokenCommand(
                user.Id,
                UserRole.OPERATOR_ADMIN.ToString(),
                operatorEntity.Id),
            CancellationToken.None);

        await action.Should().ThrowAsync<ForbiddenException>();
        await firebase.DidNotReceive().CreateOperatorCustomTokenAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task SuspendedOperator_IsRejectedBeforeFirebaseCall()
    {
        var operatorEntity = ApprovedOperator();
        operatorEntity.Suspend("security hold", DateTimeOffset.UtcNow);
        var user = ActiveOperatorAdmin(operatorEntity.Id);
        var users = Substitute.For<IUserRepository>();
        var operators = Substitute.For<IOperatorRepository>();
        var firebase = Substitute.For<IFirebaseAuthService>();
        users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        operators.GetByIdNoTrackingAsync(operatorEntity.Id, Arg.Any<CancellationToken>())
            .Returns(operatorEntity);
        var handler = new CreateFirebaseCustomTokenCommandHandler(users, operators, firebase);

        var action = () => handler.Handle(
            new CreateFirebaseCustomTokenCommand(
                user.Id,
                UserRole.OPERATOR_ADMIN.ToString(),
                operatorEntity.Id),
            CancellationToken.None);

        await action.Should().ThrowAsync<ForbiddenException>();
        await firebase.DidNotReceive().CreateOperatorCustomTokenAsync(
            Arg.Any<Guid>(),
            Arg.Any<Guid>(),
            Arg.Any<CancellationToken>());
    }

    private static Operator ApprovedOperator()
        => Operator.CreateApproved(
            "Test Operator",
            "REG-FIREBASE",
            "TAX-FIREBASE",
            "operator@example.com",
            "+84901234567",
            Guid.NewGuid(),
            DateTimeOffset.UtcNow);

    private static User ActiveOperatorAdmin(Guid operatorId)
    {
        var user = User.CreateOperatorAdminPendingEmailVerification(
            "admin@example.com",
            PhoneNumber.Parse("+84901234568"),
            "hash",
            "Operator Admin",
            operatorId);
        user.VerifyEmail();
        return user;
    }
}
