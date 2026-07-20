using FluentAssertions;
using Hangfire;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NSubstitute;
using VietRide.Booking.Application.Abstractions.Jobs;
using VietRide.Booking.Infrastructure.Jobs;

namespace VietRide.Booking.IntegrationTests.Jobs;

public sealed class Day24NoShowRegistrationTests
{
    [Fact]
    public void SchedulerRegistersOneUtcFiveMinuteJobOnBookingQueue()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] =
                "Host=localhost;Port=5432;Database=vietride_booking;Username=vietride;Password=vietride_dev",
        }).Build();
        var manager = Substitute.For<IRecurringJobManager>();
        var services = new ServiceCollection();
        services.AddSingleton(manager);
        services.AddBookingHangfire(configuration);
        using var provider = services.BuildServiceProvider();

        provider.GetRequiredService<INoShowDetectionScheduler>().EnsureScheduled();

        var registration = manager.ReceivedCalls().Should().ContainSingle(call =>
            call.GetMethodInfo().Name == nameof(IRecurringJobManager.AddOrUpdate)).Subject;
        registration.GetArguments()[0].Should().Be("booking-passenger-no-show-detection");
        registration.GetArguments().Should().Contain(Cron.MinuteInterval(5));
        registration.GetArguments().OfType<RecurringJobOptions>().Single().TimeZone.Should().Be(TimeZoneInfo.Utc);
        typeof(NoShowDetectionJob).GetMethod(nameof(NoShowDetectionJob.ExecuteAsync))!
            .GetCustomAttributes(typeof(QueueAttribute), false).Cast<QueueAttribute>().Single().Queue.Should().Be("booking");
    }
}
