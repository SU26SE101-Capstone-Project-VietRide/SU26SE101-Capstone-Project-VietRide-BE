using FluentAssertions;
using NSubstitute;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Features.Users.UpdateAvatar;
using VietRide.Identity.Domain.Entities;
using VietRide.Shared.Application.Security;
using VietRide.Shared.Kernel.ValueObjects;

namespace VietRide.Identity.UnitTests.Application.Users;

public sealed class UpdateAvatarCommandHandlerTests
{
    private const string Bucket = "vietride-test.firebasestorage.app";

    [Fact]
    public async Task ActiveUser_WithOwnedFirebaseUrl_UpdatesAvatar()
    {
        var user = ActivePassenger();
        var users = Substitute.For<IUserRepository>();
        users.GetByIdAsync(user.Id, Arg.Any<CancellationToken>()).Returns(user);
        var url =
            $"https://firebasestorage.googleapis.com/v0/b/{Bucket}/o/avatars%2F{user.Id:D}%2Favatar.webp?alt=media";
        var validator = new UpdateAvatarCommandValidator(
            new FirebaseStorageImageUrlValidator(Bucket));
        validator.Validate(new UpdateAvatarCommand(user.Id, url)).IsValid.Should().BeTrue();

        var result = await new UpdateAvatarCommandHandler(users).Handle(
            new UpdateAvatarCommand(user.Id, url),
            CancellationToken.None);

        result.AvatarUrl.Should().Be(url);
        users.Received(1).Update(user);
    }

    [Fact]
    public void CrossOwnerFirebaseUrl_IsRejected()
    {
        var userId = Guid.NewGuid();
        var otherId = Guid.NewGuid();
        var url =
            $"https://firebasestorage.googleapis.com/v0/b/{Bucket}/o/avatars%2F{otherId:D}%2Favatar.webp?alt=media";
        var validator = new UpdateAvatarCommandValidator(
            new FirebaseStorageImageUrlValidator(Bucket));

        validator.Validate(new UpdateAvatarCommand(userId, url)).IsValid.Should().BeFalse();
    }

    private static User ActivePassenger()
    {
        var user = User.CreatePassenger(
            "avatar@example.com",
            PhoneNumber.Parse("+84901234567"),
            "hash",
            "Avatar User");
        user.VerifyEmail();
        return user;
    }
}
