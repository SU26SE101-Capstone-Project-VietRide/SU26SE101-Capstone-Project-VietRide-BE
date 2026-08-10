using System.Text.Json;
using FluentAssertions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Infrastructure;

namespace VietRide.Identity.IntegrationTests.Persistence;

public sealed class OperatorRepositoryPersistenceTests : IClassFixture<UserDeviceRepositoryTests.IdentityPersistenceFixture>
{
    private readonly UserDeviceRepositoryTests.IdentityPersistenceFixture _fixture;

    public OperatorRepositoryPersistenceTests(UserDeviceRepositoryTests.IdentityPersistenceFixture fixture)
    {
        _fixture = fixture;
    }

    [Fact]
    public async Task GetByIdAsync_MaterializesJsonbPoliciesAsRawJsonStrings()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var repository = scope.ServiceProvider.GetRequiredService<IOperatorRepository>();
        var operatorTenant = Operator.CreateApproved(
            $"Operator {Guid.NewGuid():N}",
            $"BRN-{Guid.NewGuid():N}"[..36],
            $"TAX-{Guid.NewGuid():N}"[..36],
            $"operator-{Guid.NewGuid():N}@example.com",
            "0901234567",
            Guid.NewGuid(),
            DateTimeOffset.UtcNow);
        var cancellationPolicy = "[{\"hoursBeforeDeparture\":2,\"feePercent\":90},{\"hoursBeforeDeparture\":24,\"feePercent\":50}]";
        var parcelNoShowPolicy = "{\"noShowFeePercent\":10,\"additionalPaymentTimeoutMinutes\":45}";
        var luggagePolicy = "{\"defaultLuggageKgPerSeat\":20}";
        operatorTenant.UpdateProfile(
            operatorTenant.Name,
            operatorTenant.ContactEmail,
            operatorTenant.ContactPhone,
            operatorTenant.LogoUrl,
            operatorTenant.AddressStreet,
            operatorTenant.AddressWard,
            operatorTenant.AddressProvince,
            operatorTenant.RepresentativeName,
            operatorTenant.RepresentativePhone,
            cancellationPolicy,
            parcelNoShowPolicy,
            luggagePolicy);

        await db.Operators.AddAsync(operatorTenant);
        await db.SaveChangesAsync();
        db.ChangeTracker.Clear();

        var result = await repository.GetByIdAsync(operatorTenant.Id, CancellationToken.None);

        result.Should().NotBeNull();
        AssertJsonEquivalent(cancellationPolicy, result!.CancellationPolicy);
        AssertJsonEquivalent(parcelNoShowPolicy, result.ParcelNoShowPolicy);
        AssertJsonEquivalent(luggagePolicy, result.LuggagePolicy);
    }

    [Fact]
    public async Task OperatorPolicyColumns_KeepJsonbStoreTypeWithJsonElementProviderMapping()
    {
        await using var scope = _fixture.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<IdentityDbContext>();
        var operatorEntity = db.Model.FindEntityType(typeof(Operator));

        operatorEntity.Should().NotBeNull();
        var policyProperties = new[]
        {
            nameof(Operator.CancellationPolicy),
            nameof(Operator.ParcelNoShowPolicy),
            nameof(Operator.LuggagePolicy),
        };

        foreach (var propertyName in policyProperties)
        {
            var property = operatorEntity!.FindProperty(propertyName)
                ?? throw new InvalidOperationException($"Operator policy property {propertyName} was not mapped.");

            property.GetColumnType().Should().Be("jsonb");
            var typeMapping = property.GetTypeMapping();
            var converter = typeMapping.Converter;

            converter.Should().NotBeNull();
            converter!.ProviderClrType.Should().Be(typeof(JsonElement?));
        }
    }

    private static void AssertJsonEquivalent(string expectedJson, string? actualJson)
    {
        actualJson.Should().NotBeNull();

        using var expected = JsonDocument.Parse(expectedJson);
        using var actual = JsonDocument.Parse(actualJson!);
        AssertJsonEquivalent(expected.RootElement, actual.RootElement);
    }

    private static void AssertJsonEquivalent(JsonElement expected, JsonElement actual)
    {
        actual.ValueKind.Should().Be(expected.ValueKind);

        switch (expected.ValueKind)
        {
            case JsonValueKind.Object:
                var expectedProperties = expected.EnumerateObject().ToList();
                actual.EnumerateObject().Should().HaveCount(expectedProperties.Count);

                foreach (var expectedProperty in expectedProperties)
                {
                    actual.TryGetProperty(expectedProperty.Name, out var actualProperty).Should().BeTrue();
                    AssertJsonEquivalent(expectedProperty.Value, actualProperty);
                }

                break;

            case JsonValueKind.Array:
                var expectedItems = expected.EnumerateArray().ToList();
                var actualItems = actual.EnumerateArray().ToList();
                actualItems.Should().HaveCount(expectedItems.Count);

                for (var i = 0; i < expectedItems.Count; i++)
                    AssertJsonEquivalent(expectedItems[i], actualItems[i]);

                break;

            default:
                actual.ToString().Should().Be(expected.ToString());
                break;
        }
    }
}
