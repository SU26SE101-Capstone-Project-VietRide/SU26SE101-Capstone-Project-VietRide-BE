using FluentAssertions;
using NSubstitute;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Features.Admin.GetOperatorDetail;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;

namespace VietRide.Identity.UnitTests.Application.Operators;

public sealed class GetOperatorDetailQueryHandlerTests
{
    [Fact]
    public async Task Handle_SystemAdmin_ReturnsCompleteProfile()
    {
        var operatorEntity = Operator.CreatePending(
            "Operator Co",
            "BRN-001",
            "TAX-001",
            "operator@example.com",
            "+84901234567",
            "1 Street",
            "Ward",
            "District",
            "Province",
            "Representative",
            "+84901234568");
        var repository = Substitute.For<IOperatorRepository>();
        repository.GetByIdNoTrackingAsync(operatorEntity.Id, Arg.Any<CancellationToken>())
            .Returns(operatorEntity);
        var handler = new GetOperatorDetailQueryHandler(repository);

        var result = await handler.Handle(
            new GetOperatorDetailQuery(UserRole.SYSTEM_ADMIN.ToString(), operatorEntity.Id),
            CancellationToken.None);

        result.OperatorId.Should().Be(operatorEntity.Id);
        result.Address.Street.Should().Be("1 Street");
        result.Address.Ward.Should().Be("Ward");
        result.Address.District.Should().Be("District");
        result.Address.Province.Should().Be("Province");
        result.RepresentativeName.Should().Be("Representative");
        result.RepresentativePhone.Should().Be("+84901234568");
        result.ParcelNoShowPolicy.GetProperty("additionalPaymentTimeoutMinutes").GetInt32().Should().Be(30);
        result.LuggagePolicy.GetProperty("defaultLuggageKgPerSeat").GetInt32().Should().Be(10);
    }

    [Fact]
    public async Task Handle_NonSystemAdmin_ThrowsForbiddenWithoutRepositoryCall()
    {
        var repository = Substitute.For<IOperatorRepository>();
        var handler = new GetOperatorDetailQueryHandler(repository);

        var act = () => handler.Handle(
            new GetOperatorDetailQuery(UserRole.OPERATOR_ADMIN.ToString(), Guid.NewGuid()),
            CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
        await repository.DidNotReceive().GetByIdNoTrackingAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_MissingOperator_ThrowsNotFound()
    {
        var repository = Substitute.For<IOperatorRepository>();
        repository.GetByIdNoTrackingAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>())
            .Returns((Operator?)null);
        var handler = new GetOperatorDetailQueryHandler(repository);

        var act = () => handler.Handle(
            new GetOperatorDetailQuery(UserRole.SYSTEM_ADMIN.ToString(), Guid.NewGuid()),
            CancellationToken.None);

        await act.Should().ThrowAsync<NotFoundException>();
    }
}
