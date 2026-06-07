using System.Text.Json;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Features.Operators;
using VietRide.Identity.Domain.Entities;
using VietRide.Shared.Application.Exceptions;
using Xunit;

namespace VietRide.Identity.UnitTests.Application.Operators;

public sealed class ProfileOperatorProfileHandlerTests
{
    [Fact]
    public async Task GetOperatorProfileAsync_WhenOperatorExists_ReturnsFullContractShapeAndDefaultPolicies()
    {
        var operatorProfile = CreateApprovedOperator();
        operatorProfile.UpdateProfile(
            "VietRide Limousine",
            operatorProfile.ContactEmail,
            "+84901234567",
            "https://cdn.vietride.app/operators/logo.png",
            "123 Le Loi",
            "Ben Nghe",
            "District 1",
            "Ho Chi Minh City",
            "Nguyen Van Operator",
            "+84907654321",
            null,
            null,
            null);
        var handler = new GetOperatorProfileHandler(new FakeOperatorRepository(operatorProfile));

        var response = await handler.Handle(new GetOperatorProfileQuery(operatorProfile.Id), CancellationToken.None);

        Assert.Equal(operatorProfile.Id, response.OperatorId);
        Assert.Equal("VietRide Limousine", response.Name);
        Assert.Equal("0312345678", response.BusinessRegistrationNumber);
        Assert.Equal("0312345678", response.TaxCode);
        Assert.Equal("ops@example.com", response.ContactEmail);
        Assert.Equal("+84901234567", response.ContactPhone);
        Assert.Equal("https://cdn.vietride.app/operators/logo.png", response.LogoUrl);
        Assert.Equal("123 Le Loi", response.Address.Street);
        Assert.Equal("Ben Nghe", response.Address.Ward);
        Assert.Equal("District 1", response.Address.District);
        Assert.Equal("Ho Chi Minh City", response.Address.Province);
        Assert.Equal("Nguyen Van Operator", response.RepresentativeName);
        Assert.Equal("+84907654321", response.RepresentativePhone);
        Assert.Equal("APPROVED", response.RegistrationStatus);
        Assert.True(response.IsActive);
        Assert.Null(response.CancellationPolicy);
        Assert.Equal(0, response.ParcelNoShowPolicy.GetProperty("noShowFeePercent").GetInt32());
        Assert.Equal(30, response.ParcelNoShowPolicy.GetProperty("additionalPaymentTimeoutMinutes").GetInt32());
        Assert.Equal(10, response.LuggagePolicy.GetProperty("defaultLuggageKgPerSeat").GetInt32());
    }

    [Fact]
    public async Task UpdateOperatorProfileAsync_WhenApprovedAdmin_PersistsProfileFieldsAndSortedPoliciesAndDefaults()
    {
        var operatorProfile = CreateApprovedOperator();
        var repository = new FakeOperatorRepository(operatorProfile);
        var handler = new UpdateOperatorProfileHandler(repository);
        var cancellationPolicy = JsonDocument.Parse(
            "[{\"hoursBeforeDeparture\":24,\"feePercent\":20},{\"hoursBeforeDeparture\":2,\"feePercent\":90}]")
            .RootElement
            .Clone();

        var response = await handler.Handle(
            CreateCommand(operatorProfile.Id, cancellationPolicy: cancellationPolicy),
            CancellationToken.None);

        Assert.Equal(1, repository.UpdateCallCount);
        Assert.Same(operatorProfile, repository.UpdatedOperator);
        Assert.Equal(operatorProfile.Id, response.OperatorId);
        Assert.Equal("Updated Operator", response.Name);
        Assert.Equal("+84909876543", response.ContactPhone);
        Assert.Equal("https://cdn.vietride.app/operators/updated.png", response.LogoUrl);
        Assert.Equal("456 Nguyen Hue", response.Address.Street);
        Assert.Equal("Ben Thanh", response.Address.Ward);
        Assert.Equal("District 1", response.Address.District);
        Assert.Equal("Ho Chi Minh City", response.Address.Province);
        Assert.Equal("Tran Van Admin", response.RepresentativeName);
        Assert.Equal("+84901112222", response.RepresentativePhone);
        Assert.NotNull(response.CancellationPolicy);
        Assert.Equal(2, response.CancellationPolicy.Value[0].GetProperty("hoursBeforeDeparture").GetInt32());
        Assert.Equal(24, response.CancellationPolicy.Value[1].GetProperty("hoursBeforeDeparture").GetInt32());
        Assert.Equal(0, response.ParcelNoShowPolicy.GetProperty("noShowFeePercent").GetInt32());
        Assert.Equal(30, response.ParcelNoShowPolicy.GetProperty("additionalPaymentTimeoutMinutes").GetInt32());
        Assert.Equal(10, response.LuggagePolicy.GetProperty("defaultLuggageKgPerSeat").GetInt32());
        Assert.Equal(response.CancellationPolicy.Value.GetRawText(), operatorProfile.CancellationPolicy);
        Assert.Equal(response.ParcelNoShowPolicy.GetRawText(), operatorProfile.ParcelNoShowPolicy);
        Assert.Equal(response.LuggagePolicy.GetRawText(), operatorProfile.LuggagePolicy);
    }

    [Fact]
    public async Task UpdateOperatorProfileAsync_WhenCallerIsOperatorStaff_ThrowsForbiddenExceptionAndDoesNotUpdate()
    {
        var operatorProfile = CreateApprovedOperator();
        var repository = new FakeOperatorRepository(operatorProfile);
        var handler = new UpdateOperatorProfileHandler(repository);

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(
            CreateCommand(operatorProfile.Id, callerRole: "OPERATOR_STAFF"),
            CancellationToken.None));

        Assert.Equal(0, repository.UpdateCallCount);
    }

    [Fact]
    public async Task UpdateOperatorProfileAsync_WhenOperatorNotApproved_ThrowsForbiddenExceptionAndDoesNotUpdate()
    {
        var operatorProfile = CreatePendingOperator();
        var repository = new FakeOperatorRepository(operatorProfile);
        var handler = new UpdateOperatorProfileHandler(repository);

        await Assert.ThrowsAsync<ForbiddenException>(() => handler.Handle(
            CreateCommand(operatorProfile.Id),
            CancellationToken.None));

        Assert.Equal(0, repository.UpdateCallCount);
    }

    [Fact]
    public async Task UpdateOperatorProfileAsync_WhenOperatorMissing_ThrowsNotFoundException()
    {
        var repository = new FakeOperatorRepository(operatorProfile: null);
        var handler = new UpdateOperatorProfileHandler(repository);

        await Assert.ThrowsAsync<NotFoundException>(() => handler.Handle(
            CreateCommand(Guid.NewGuid()),
            CancellationToken.None));

        Assert.Equal(0, repository.UpdateCallCount);
    }

    [Fact]
    public void UpdateOperatorProfileValidator_WhenPolicyShapeInvalid_ReturnsValidationError()
    {
        var validator = new UpdateOperatorProfileValidator();
        var invalidCancellationPolicy = JsonDocument.Parse("{\"hoursBeforeDeparture\":2,\"feePercent\":90}")
            .RootElement
            .Clone();

        var result = validator.Validate(CreateCommand(Guid.NewGuid(), cancellationPolicy: invalidCancellationPolicy));

        Assert.False(result.IsValid);
        Assert.Contains(result.Errors, error => error.PropertyName == nameof(UpdateOperatorProfileCommand.CancellationPolicy));
    }

    private static UpdateOperatorProfileCommand CreateCommand(
        Guid operatorId,
        string callerRole = "OPERATOR_ADMIN",
        JsonElement? cancellationPolicy = null)
    {
        return new UpdateOperatorProfileCommand(
            operatorId,
            callerRole,
            "Updated Operator",
            "0909876543",
            "https://cdn.vietride.app/operators/updated.png",
            "456 Nguyen Hue",
            "Ben Thanh",
            "District 1",
            "Ho Chi Minh City",
            "Tran Van Admin",
            "0901112222",
            cancellationPolicy,
            null,
            null);
    }

    private static Operator CreateApprovedOperator()
    {
        var operatorProfile = CreatePendingOperator();
        operatorProfile.Approve(Guid.NewGuid(), DateTimeOffset.UtcNow);
        return operatorProfile;
    }

    private static Operator CreatePendingOperator()
    {
        return Operator.CreatePending(
            "VietRide Limousine",
            "0312345678",
            "0312345678",
            "ops@example.com",
            "+84901234567",
            "123 Le Loi",
            "Ben Nghe",
            "District 1",
            "Ho Chi Minh City",
            "Nguyen Van Operator",
            "+84907654321");
    }

    private sealed class FakeOperatorRepository : IOperatorRepository
    {
        private readonly Operator? operatorProfile;

        public FakeOperatorRepository(Operator? operatorProfile)
        {
            this.operatorProfile = operatorProfile;
        }

        public int UpdateCallCount { get; private set; }

        public Operator? UpdatedOperator { get; private set; }

        public Task<Operator?> GetByBusinessRegistrationNumberAsync(
            string businessRegistrationNumber,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(operatorProfile?.BusinessRegistrationNumber == businessRegistrationNumber ? operatorProfile : null);
        }

        public Task<Operator?> GetByTaxCodeAsync(string taxCode, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(operatorProfile?.TaxCode == taxCode ? operatorProfile : null);
        }

        public Task<Operator?> GetByIdAsync(Guid id, CancellationToken ct)
        {
            return Task.FromResult(operatorProfile?.Id == id ? operatorProfile : null);
        }

        public Task<Operator> AddAsync(Operator entity, CancellationToken ct)
        {
            throw new NotSupportedException();
        }

        public void Update(Operator entity)
        {
            UpdateCallCount++;
            UpdatedOperator = entity;
        }

        public void Remove(Operator entity)
        {
            throw new NotSupportedException();
        }

        public IQueryable<Operator> Query()
        {
            return AsQueryable();
        }

        public IQueryable<Operator> QueryNoTracking()
        {
            return AsQueryable();
        }

        private IQueryable<Operator> AsQueryable()
        {
            return operatorProfile is null
                ? Enumerable.Empty<Operator>().AsQueryable()
                : new[] { operatorProfile }.AsQueryable();
        }
    }
}
