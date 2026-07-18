using FluentAssertions;
using NSubstitute;
using VietRide.Identity.Application.Abstractions.Repositories;
using VietRide.Identity.Application.Features.Internal.Operators.GetOperatorSummaries;
using VietRide.Identity.Domain.Entities;

namespace VietRide.Identity.UnitTests.Application.Internal.Operators.GetOperatorSummaries;

public sealed class GetOperatorSummariesTests
{
    [Fact]
    public async Task Handler_ReturnsFoundOperatorsInDeterministicIdOrder()
    {
        var first = CreateOperator("First");
        var second = CreateOperator("Second");
        var expected = new[] { first, second }.OrderBy(operatorTenant => operatorTenant.Id).ToArray();
        var repository = Substitute.For<IOperatorRepository>();
        repository.ListSummariesByIdsAsync(
                Arg.Any<IReadOnlyCollection<Guid>>(),
                Arg.Any<CancellationToken>())
            .Returns(expected.Reverse().ToArray());
        var handler = new GetOperatorSummariesQueryHandler(repository);

        var result = await handler.Handle(
            new GetOperatorSummariesQuery([first.Id, second.Id, Guid.NewGuid()]),
            CancellationToken.None);

        result.Should().AllBeOfType<InternalOperatorSummaryDto>();
        result.Select(item => item.OperatorId).Should().Equal(expected.Select(item => item.Id));
        result.Select(item => item.OperatorName).Should().Equal(expected.Select(item => item.Name));
    }

    [Fact]
    public async Task Handler_EmptyInput_ReturnsEmptyWithoutRepositoryCall()
    {
        var repository = Substitute.For<IOperatorRepository>();
        var handler = new GetOperatorSummariesQueryHandler(repository);

        var result = await handler.Handle(new GetOperatorSummariesQuery([]), CancellationToken.None);

        result.Should().BeEmpty();
        await repository.DidNotReceive().ListSummariesByIdsAsync(
            Arg.Any<IReadOnlyCollection<Guid>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public void Validator_RejectsMoreThanFiveHundredDuplicateOrEmptyIds()
    {
        var validator = new GetOperatorSummariesQueryValidator();

        validator.Validate(new GetOperatorSummariesQuery(
                Enumerable.Range(0, 501).Select(_ => Guid.NewGuid()).ToArray()))
            .IsValid.Should().BeFalse();
        validator.Validate(new GetOperatorSummariesQuery([Guid.NewGuid(), Guid.Empty]))
            .IsValid.Should().BeFalse();
        var duplicate = Guid.NewGuid();
        validator.Validate(new GetOperatorSummariesQuery([duplicate, duplicate]))
            .IsValid.Should().BeFalse();
    }

    private static Operator CreateOperator(string name)
        => Operator.CreatePending(
            name,
            $"BR-{Guid.NewGuid():N}",
            $"TAX-{Guid.NewGuid():N}",
            $"{Guid.NewGuid():N}@example.com",
            "+84901234567");
}
