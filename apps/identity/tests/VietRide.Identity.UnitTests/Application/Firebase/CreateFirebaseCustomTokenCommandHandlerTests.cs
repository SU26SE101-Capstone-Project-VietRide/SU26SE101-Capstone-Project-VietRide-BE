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
    public async Task ActivePassenger_ReceivesParcelPhotoScopedToken()
    {
        var user = ActivePassenger();
        var users = Substitute.For<IUserRepository>();
        var operators = Substitute.For<IOperatorRepository>();
        var firebase = Substitute.For<IFirebaseAuthService>();
        users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        firebase.CreateCustomTokenAsync(
                user.Id,
                UserRole.PASSENGER.ToString(),
                null,
                FirebaseUploadPurpose.PARCEL_PHOTO.ToString(),
                Arg.Any<CancellationToken>())
            .Returns("passenger-token");
        var handler = new CreateFirebaseCustomTokenCommandHandler(users, operators, firebase);

        var result = await handler.Handle(
            new CreateFirebaseCustomTokenCommand(
                user.Id,
                UserRole.PASSENGER.ToString(),
                null,
                FirebaseUploadPurpose.PARCEL_PHOTO.ToString()),
            CancellationToken.None);

        result.Token.Should().Be("passenger-token");
        result.UploadPath.Should().Be($"parcels/{user.Id:D}/");
    }

    [Fact]
    public async Task Passenger_RequestingVehicleImage_IsForbidden()
    {
        var user = ActivePassenger();
        var users = Substitute.For<IUserRepository>();
        users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        var handler = new CreateFirebaseCustomTokenCommandHandler(
            users,
            Substitute.For<IOperatorRepository>(),
            Substitute.For<IFirebaseAuthService>());

        var action = () => handler.Handle(
            new CreateFirebaseCustomTokenCommand(
                user.Id,
                UserRole.PASSENGER.ToString(),
                null,
                FirebaseUploadPurpose.VEHICLE_IMAGE.ToString()),
            CancellationToken.None);

        await action.Should().ThrowAsync<ForbiddenException>();
    }

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
        firebase.CreateCustomTokenAsync(
                user.Id,
                UserRole.OPERATOR_ADMIN.ToString(),
                operatorEntity.Id,
                FirebaseUploadPurpose.VEHICLE_IMAGE.ToString(),
                Arg.Any<CancellationToken>())
            .Returns("custom-token");
        var handler = new CreateFirebaseCustomTokenCommandHandler(users, operators, firebase);

        var result = await handler.Handle(
            new CreateFirebaseCustomTokenCommand(
                user.Id,
                UserRole.OPERATOR_ADMIN.ToString(),
                operatorEntity.Id),
            CancellationToken.None);

        result.Token.Should().Be("custom-token");
        result.Purpose.Should().Be("VEHICLE_IMAGE");
        result.UploadPath.Should().Be($"vehicles/{operatorEntity.Id:D}/");
        await firebase.Received(1).CreateCustomTokenAsync(
            user.Id,
            UserRole.OPERATOR_ADMIN.ToString(),
            operatorEntity.Id,
            FirebaseUploadPurpose.VEHICLE_IMAGE.ToString(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ActiveAdminOfApprovedOperator_ReceivesOperatorLogoScopedToken()
    {
        var operatorEntity = ApprovedOperator();
        var user = ActiveOperatorAdmin(operatorEntity.Id);
        var users = Substitute.For<IUserRepository>();
        var operators = Substitute.For<IOperatorRepository>();
        var firebase = Substitute.For<IFirebaseAuthService>();
        users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        operators.GetByIdNoTrackingAsync(operatorEntity.Id, Arg.Any<CancellationToken>())
            .Returns(operatorEntity);
        firebase.CreateCustomTokenAsync(
                user.Id,
                UserRole.OPERATOR_ADMIN.ToString(),
                operatorEntity.Id,
                FirebaseUploadPurpose.OPERATOR_LOGO.ToString(),
                Arg.Any<CancellationToken>())
            .Returns("logo-token");
        var handler = new CreateFirebaseCustomTokenCommandHandler(users, operators, firebase);

        var result = await handler.Handle(
            new CreateFirebaseCustomTokenCommand(
                user.Id,
                UserRole.OPERATOR_ADMIN.ToString(),
                operatorEntity.Id,
                FirebaseUploadPurpose.OPERATOR_LOGO.ToString()),
            CancellationToken.None);

        result.Token.Should().Be("logo-token");
        result.UploadPath.Should().Be($"operators/{operatorEntity.Id:D}/logo/");
    }

    [Theory]
    [InlineData(UserRole.DRIVER)]
    [InlineData(UserRole.ASSISTANT)]
    public async Task ActiveDriverOrAssistant_ReceivesIncidentPhotoScopedToken(UserRole role)
    {
        var operatorEntity = ApprovedOperator();
        var user = ActiveOperatorMember(operatorEntity.Id, role);
        var users = Substitute.For<IUserRepository>();
        var operators = Substitute.For<IOperatorRepository>();
        var firebase = Substitute.For<IFirebaseAuthService>();
        users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        operators.GetByIdNoTrackingAsync(operatorEntity.Id, Arg.Any<CancellationToken>())
            .Returns(operatorEntity);
        firebase.CreateCustomTokenAsync(
                user.Id,
                role.ToString(),
                operatorEntity.Id,
                FirebaseUploadPurpose.INCIDENT_PHOTO.ToString(),
                Arg.Any<CancellationToken>())
            .Returns("incident-token");
        var handler = new CreateFirebaseCustomTokenCommandHandler(users, operators, firebase);

        var result = await handler.Handle(
            new CreateFirebaseCustomTokenCommand(
                user.Id,
                role.ToString(),
                operatorEntity.Id,
                FirebaseUploadPurpose.INCIDENT_PHOTO.ToString()),
            CancellationToken.None);

        result.Token.Should().Be("incident-token");
        result.UploadPath.Should().Be($"incidents/{operatorEntity.Id:D}/{user.Id:D}/");
    }

    [Fact]
    public async Task ActiveAssistant_ReceivesParcelEvidencePhotoScopedToken()
    {
        var operatorEntity = ApprovedOperator();
        var user = ActiveOperatorMember(operatorEntity.Id, UserRole.ASSISTANT);
        var users = Substitute.For<IUserRepository>();
        var operators = Substitute.For<IOperatorRepository>();
        var firebase = Substitute.For<IFirebaseAuthService>();
        users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        operators.GetByIdNoTrackingAsync(operatorEntity.Id, Arg.Any<CancellationToken>())
            .Returns(operatorEntity);
        firebase.CreateCustomTokenAsync(
                user.Id,
                UserRole.ASSISTANT.ToString(),
                operatorEntity.Id,
                FirebaseUploadPurpose.PARCEL_EVIDENCE_PHOTO.ToString(),
                Arg.Any<CancellationToken>())
            .Returns("parcel-evidence-token");
        var handler = new CreateFirebaseCustomTokenCommandHandler(users, operators, firebase);

        var result = await handler.Handle(
            new CreateFirebaseCustomTokenCommand(
                user.Id,
                UserRole.ASSISTANT.ToString(),
                operatorEntity.Id,
                FirebaseUploadPurpose.PARCEL_EVIDENCE_PHOTO.ToString()),
            CancellationToken.None);

        result.Token.Should().Be("parcel-evidence-token");
        result.Purpose.Should().Be("PARCEL_EVIDENCE_PHOTO");
        result.UploadPath.Should().Be($"parcel-ops/{operatorEntity.Id:D}/{user.Id:D}/");
    }

    [Fact]
    public async Task ActivePassenger_ReceivesOwnAvatarScopedToken()
    {
        var user = ActivePassenger();
        var users = Substitute.For<IUserRepository>();
        var firebase = Substitute.For<IFirebaseAuthService>();
        users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        firebase.CreateCustomTokenAsync(
                user.Id,
                UserRole.PASSENGER.ToString(),
                null,
                FirebaseUploadPurpose.USER_AVATAR.ToString(),
                Arg.Any<CancellationToken>())
            .Returns("avatar-token");
        var handler = new CreateFirebaseCustomTokenCommandHandler(
            users,
            Substitute.For<IOperatorRepository>(),
            firebase);

        var result = await handler.Handle(
            new CreateFirebaseCustomTokenCommand(
                user.Id,
                UserRole.PASSENGER.ToString(),
                null,
                FirebaseUploadPurpose.USER_AVATAR.ToString()),
            CancellationToken.None);

        result.Token.Should().Be("avatar-token");
        result.UploadPath.Should().Be($"avatars/{user.Id:D}/");
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
        await firebase.DidNotReceive().CreateCustomTokenAsync(
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<Guid?>(),
            Arg.Any<string>(),
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
        await firebase.DidNotReceive().CreateCustomTokenAsync(
            Arg.Any<Guid>(),
            Arg.Any<string>(),
            Arg.Any<Guid?>(),
            Arg.Any<string>(),
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

    private static User ActivePassenger()
    {
        var user = User.CreatePassenger(
            "passenger.firebase@example.com",
            PhoneNumber.Parse("+84901234569"),
            "hash",
            "Passenger");
        user.VerifyEmail();
        return user;
    }

    private static User ActiveOperatorMember(Guid operatorId, UserRole role)
    {
        var user = User.CreateOperatorScopedPendingPassword(
            $"{role.ToString().ToLowerInvariant()}@example.com",
            PhoneNumber.Parse(role == UserRole.DRIVER ? "+84901234570" : "+84901234571"),
            role.ToString(),
            role,
            operatorId);
        user.SetInitialPassword("hash");
        return user;
    }
}
