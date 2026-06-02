using System.Reflection;
using FluentAssertions;
using NetArchTest.Rules;
using VietRide.Identity.Infrastructure;

namespace VietRide.Identity.UnitTests.Architecture;

/// <summary>
/// NetArchTest dependency-direction rules for the Identity service.
/// CI-enforced per BSOT §3.2.2 / AGENTS.md architecture invariants:
///   Domain      → (nothing from Application/Infrastructure/Api)
///   Application → Domain only (not Infrastructure/Api)
///   Infrastructure → Domain + Application (not Api)
///   Api         → Application + Infrastructure (composition root)
/// </summary>
public sealed class LayeringTests
{
    private const string DomainNs = "VietRide.Identity.Domain";
    private const string ApplicationNs = "VietRide.Identity.Application";
    private const string InfrastructureNs = "VietRide.Identity.Infrastructure";
    private const string ApiNs = "VietRide.Identity.Api";

    // Use concrete types from assemblies that already have source files.
    // Domain and Application are empty before Task 3.1/3.4 land — load by name
    // to avoid compile errors when those assemblies have no exported types yet.
    private static readonly Assembly InfrastructureAssembly =
        typeof(IdentityDbContext).Assembly;

    private static readonly Assembly ApiAssembly =
        typeof(VietRide.Identity.Api.Controllers.PingController).Assembly;

    // Load Domain + Application by assembly name at runtime so the test file
    // compiles even when those projects have no exported user types yet.
    private static Assembly LoadByName(string shortName)
    {
        // The assembly is already loaded because the UnitTests project references
        // both projects — AppDomain will have it.
        var loaded = AppDomain.CurrentDomain
            .GetAssemblies()
            .FirstOrDefault(a => a.GetName().Name == shortName);

        if (loaded is not null)
        {
            return loaded;
        }

        // Fallback: load from the same directory as this test assembly.
        var dir = Path.GetDirectoryName(typeof(LayeringTests).Assembly.Location)!;
        return Assembly.LoadFrom(Path.Combine(dir, shortName + ".dll"));
    }

    private static readonly Assembly DomainAssembly =
        LoadByName("VietRide.Identity.Domain");

    private static readonly Assembly ApplicationAssembly =
        LoadByName("VietRide.Identity.Application");

    [Fact]
    public void Domain_should_not_reference_Application_or_Infrastructure_or_Api()
    {
        var result = Types.InAssembly(DomainAssembly)
            .Should()
            .NotHaveDependencyOnAny(ApplicationNs, InfrastructureNs, ApiNs)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Domain must be a pure POCO layer with zero references to outer layers. " +
            "Failing types: {0}",
            result.FailingTypeNames is not null
                ? string.Join(", ", result.FailingTypeNames)
                : "none");
    }

    [Fact]
    public void Application_should_not_reference_Infrastructure_or_Api()
    {
        var result = Types.InAssembly(ApplicationAssembly)
            .Should()
            .NotHaveDependencyOnAny(InfrastructureNs, ApiNs)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Application must not reference Infrastructure or Api. " +
            "Failing types: {0}",
            result.FailingTypeNames is not null
                ? string.Join(", ", result.FailingTypeNames)
                : "none");
    }

    [Fact]
    public void Infrastructure_should_not_reference_Api()
    {
        var result = Types.InAssembly(InfrastructureAssembly)
            .Should()
            .NotHaveDependencyOn(ApiNs)
            .GetResult();

        result.IsSuccessful.Should().BeTrue(
            "Infrastructure must not reference Api. " +
            "Failing types: {0}",
            result.FailingTypeNames is not null
                ? string.Join(", ", result.FailingTypeNames)
                : "none");
    }

    [Fact]
    public void Api_controllers_should_exist_in_Api_assembly()
    {
        // Verifies the Api assembly is correctly wired and the PingController
        // is present as a baseline. Deeper Controller→MediatR→Handler chain
        // is tested in integration tests (Task 3.4).
        var types = Types.InAssembly(ApiAssembly)
            .That()
            .HaveNameEndingWith("Controller")
            .GetTypes();

        types.Should().NotBeEmpty(
            "the Api assembly must contain at least one controller (PingController).");
    }
}
