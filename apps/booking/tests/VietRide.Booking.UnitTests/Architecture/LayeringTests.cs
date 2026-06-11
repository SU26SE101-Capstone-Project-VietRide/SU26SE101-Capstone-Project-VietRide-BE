using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using VietRide.Booking.Application;
using VietRide.Booking.Infrastructure;

namespace VietRide.Booking.UnitTests.Architecture;

public sealed class LayeringTests
{
    private static readonly Assembly DomainAssembly = Assembly.Load("VietRide.Booking.Domain");
    private static readonly Assembly ApplicationAssembly = typeof(ApplicationAssemblyMarker).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(BookingDbContext).Assembly;
    private static readonly Assembly ApiAssembly = typeof(Program).Assembly;

    [Fact]
    public void Domain_Should_Not_Depend_On_Other_Project_Layers()
    {
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "VietRide.Booking.Application",
                "VietRide.Booking.Infrastructure",
                "VietRide.Booking.Api")
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Application_Should_Depend_Only_On_Domain_Project_Layer()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "VietRide.Booking.Infrastructure",
                "VietRide.Booking.Api")
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Infrastructure_Should_Depend_Only_On_Domain_And_Application_Project_Layers()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .ShouldNot()
            .HaveDependencyOn("VietRide.Booking.Api")
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Api_Should_Depend_Only_On_Application_And_Infrastructure_Project_Layers()
    {
        var result = Types.InAssembly(ApiAssembly)
            .ShouldNot()
            .HaveDependencyOn("VietRide.Booking.Domain")
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }
}
