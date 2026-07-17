using FluentAssertions;
using Hangfire;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using VietRide.Booking.Application.Abstractions.Jobs;
using VietRide.Booking.Infrastructure.Jobs;

namespace VietRide.Booking.IntegrationTests.Jobs;

public sealed class Day23ScheduleChangeTimeoutRegistrationTests
{
    [Fact]
    public void RegistrationUsesBookingQueueAndSeparateScheduler()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Default"] =
                    "Host=localhost;Port=5432;Database=vietride_booking;Username=vietride;Password=vietride_dev",
            })
            .Build();
        var services = new ServiceCollection();

        services.AddBookingHangfire(configuration);

        services.Should().Contain(descriptor =>
            descriptor.ServiceType == typeof(IScheduleChangeAutoAcceptScheduler)
            && descriptor.ImplementationType == typeof(HangfireScheduleChangeAutoAcceptScheduler)
            && descriptor.Lifetime == ServiceLifetime.Scoped);
        typeof(ScheduleChangeAutoAcceptJob).GetMethod(nameof(ScheduleChangeAutoAcceptJob.ExecuteAsync))!
            .GetCustomAttributes(typeof(QueueAttribute), false)
            .Cast<QueueAttribute>().Single().Queue.Should().Be("booking");
    }
}
