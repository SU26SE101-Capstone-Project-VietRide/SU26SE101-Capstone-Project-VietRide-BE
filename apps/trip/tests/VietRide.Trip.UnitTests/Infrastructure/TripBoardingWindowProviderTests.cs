using FluentAssertions;
using Microsoft.Extensions.Configuration;
using VietRide.Trip.Infrastructure.Services;

namespace VietRide.Trip.UnitTests.Infrastructure;

public sealed class TripBoardingWindowProviderTests
{
    [Fact]
    public void Create_WhenMissing_UsesThreeHourDefault()
    {
        var provider = TripBoardingWindowProvider.Create(new ConfigurationBuilder().Build());

        provider.ManualEarlyWindow.Should().Be(TimeSpan.FromMinutes(180));
    }

    [Fact]
    public void Create_WhenConfigured_UsesPositiveMinuteValue()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [TripBoardingWindowProvider.ConfigurationKey] = "240",
            })
            .Build();

        var provider = TripBoardingWindowProvider.Create(configuration);

        provider.ManualEarlyWindow.Should().Be(TimeSpan.FromMinutes(240));
    }

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    [InlineData("invalid")]
    public void Create_WhenConfiguredInvalid_FailsFast(string value)
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                [TripBoardingWindowProvider.ConfigurationKey] = value,
            })
            .Build();

        var action = () => TripBoardingWindowProvider.Create(configuration);

        action.Should().Throw<InvalidOperationException>()
            .WithMessage($"*{TripBoardingWindowProvider.ConfigurationKey}*");
    }
}
