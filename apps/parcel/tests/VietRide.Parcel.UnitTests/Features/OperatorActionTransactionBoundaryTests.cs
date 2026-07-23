using System.Reflection;
using FluentAssertions;
using VietRide.Parcel.Application.Features.Parcels.OperatorActions;
using VietRide.Shared.Application.Behaviors;

namespace VietRide.Parcel.UnitTests.Features;

public sealed class OperatorActionTransactionBoundaryTests
{
    [Theory]
    [InlineData(typeof(ConfirmRefundCommand))]
    [InlineData(typeof(OverrideCapacityCommand))]
    public void CommandsWithHandlerOwnedTransaction_SkipPipelineTransaction(Type commandType)
    {
        commandType.GetCustomAttribute<SkipTransactionAttribute>()
            .Should().NotBeNull();
    }
}
