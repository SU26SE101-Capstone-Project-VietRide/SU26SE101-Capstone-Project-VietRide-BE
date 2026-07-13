using VietRide.Identity.Application.Features.InternalUsers.GetInternalUserByPhone;

namespace VietRide.Identity.UnitTests.Application.Internal.Users;

public sealed class GetInternalUserByPhoneQueryValidatorTests
{
    [Theory]
    [InlineData("+84901234567", true)]
    [InlineData("0901234567", false)]
    [InlineData(" +84901234567 ", false)]
    [InlineData("+84 901234567", false)]
    [InlineData("+84-901234567", false)]
    [InlineData("(+84)901234567", false)]
    public void Validate_EnforcesCanonicalE164(string phone, bool expected)
    {
        var result = new GetInternalUserByPhoneQueryValidator()
            .Validate(new GetInternalUserByPhoneQuery(phone));
        Assert.Equal(expected, result.IsValid);
    }
}
