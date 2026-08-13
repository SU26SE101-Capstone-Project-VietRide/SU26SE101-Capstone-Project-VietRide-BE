using VietRide.Identity.Application.Features.InternalUsers.GetInternalUserByEmail;

namespace VietRide.Identity.UnitTests.Application.Internal.Users;

public sealed class GetInternalUserByEmailQueryValidatorTests
{
    [Theory]
    [InlineData("recipient@example.com", true)]
    [InlineData(" Recipient@Example.COM ", true)]
    [InlineData("not-an-email", false)]
    [InlineData("", false)]
    public void Validate_EnforcesEmailShape(string email, bool expected)
    {
        var result = new GetInternalUserByEmailQueryValidator()
            .Validate(new GetInternalUserByEmailQuery(email));

        Assert.Equal(expected, result.IsValid);
    }
}
