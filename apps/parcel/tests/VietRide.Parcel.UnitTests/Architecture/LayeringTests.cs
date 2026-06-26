using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using VietRide.Parcel.Application;
using VietRide.Parcel.Infrastructure;

namespace VietRide.Parcel.UnitTests.Architecture;

public sealed class LayeringTests
{
    private static readonly Assembly DomainAssembly = Assembly.Load("VietRide.Parcel.Domain");
    private static readonly Assembly ApplicationAssembly = typeof(ApplicationAssemblyMarker).Assembly;
    private static readonly Assembly InfrastructureAssembly = typeof(ParcelDbContext).Assembly;
    private static readonly Assembly ApiAssembly = typeof(Program).Assembly;

    [Fact]
    public void Domain_Should_Not_Depend_On_Other_Project_Layers()
    {
        var result = Types.InAssembly(DomainAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "VietRide.Parcel.Application",
                "VietRide.Parcel.Infrastructure",
                "VietRide.Parcel.Api")
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Application_Should_Depend_Only_On_Domain_Project_Layer()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .ShouldNot()
            .HaveDependencyOnAny(
                "VietRide.Parcel.Infrastructure",
                "VietRide.Parcel.Api")
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Infrastructure_Should_Depend_Only_On_Domain_And_Application_Project_Layers()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .ShouldNot()
            .HaveDependencyOn("VietRide.Parcel.Api")
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }

    [Fact]
    public void Api_Should_Depend_Only_On_Application_And_Infrastructure_Project_Layers()
    {
        var result = Types.InAssembly(ApiAssembly)
            .ShouldNot()
            .HaveDependencyOn("VietRide.Parcel.Domain")
            .GetResult();

        result.IsSuccessful.Should().BeTrue();
    }
}
