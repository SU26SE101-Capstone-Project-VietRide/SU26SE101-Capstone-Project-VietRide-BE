using FluentAssertions;
using NSubstitute;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Features.Admin.ListOperators;
using VietRide.Identity.Domain.Entities;
using VietRide.Identity.Domain.Enums;
using VietRide.Shared.Application.Exceptions;
using VietRide.Shared.Kernel.Primitives;

namespace VietRide.Identity.UnitTests.Application.Operators;

public sealed class ListOperatorsQueryHandlerTests
{
    [Fact]
    public async Task Handle_SystemAdmin_ReturnsPagedOperatorDtos()
    {
        var operators = Substitute.For<IOperatorRepository>();
        var operatorEntity = Operator.CreatePending(
            "VietRide Limousine",
            "0312345678",
            "0312345678",
            "ops@example.com",
            "+84901234567");
        operators.ListAsync(Arg.Any<QueryOptions>(), OperatorRegistrationStatus.PENDING, Arg.Any<CancellationToken>())
            .Returns(PagedResult<Operator>.Create([operatorEntity], 1, 20, 1));
        var handler = new ListOperatorsQueryHandler(operators);

        var result = await handler.Handle(
            new ListOperatorsQuery(
                UserRole.SYSTEM_ADMIN.ToString(),
                Page: 1,
                PageSize: 20,
                Search: "0312345678",
                SortBy: "name",
                SortDir: "asc",
                Status: "PENDING"),
            CancellationToken.None);

        result.Page.Should().Be(1);
        result.PageSize.Should().Be(20);
        result.TotalItems.Should().Be(1);
        var item = result.Items.Should().ContainSingle().Subject;
        item.OperatorId.Should().Be(operatorEntity.Id);
        item.Name.Should().Be("VietRide Limousine");
        item.ContactEmail.Should().Be("ops@example.com");
        item.ContactPhone.Should().Be("+84901234567");
        item.BusinessRegistrationNumber.Should().Be("0312345678");
        item.TaxCode.Should().Be("0312345678");
        item.RegistrationStatus.Should().Be(OperatorRegistrationStatus.PENDING.ToString());
        item.IsActive.Should().BeTrue();
        await operators.Received(1).ListAsync(
            Arg.Is<QueryOptions>(options =>
                options.Page == 1
                && options.PageSize == 20
                && options.Search == "0312345678"
                && options.SortBy == "name"
                && options.SortDir == "asc"),
            OperatorRegistrationStatus.PENDING,
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_NonSystemAdmin_ThrowsForbidden()
    {
        var operators = Substitute.For<IOperatorRepository>();
        var handler = new ListOperatorsQueryHandler(operators);

        var act = () => handler.Handle(
            new ListOperatorsQuery(
                UserRole.OPERATOR_ADMIN.ToString(),
                Page: null,
                PageSize: null,
                Search: null,
                SortBy: null,
                SortDir: null,
                Status: null),
            CancellationToken.None);

        await act.Should().ThrowAsync<ForbiddenException>();
        await operators.DidNotReceive().ListAsync(Arg.Any<QueryOptions>(), Arg.Any<OperatorRegistrationStatus?>(), Arg.Any<CancellationToken>());
    }
}
