using Xunit;

namespace VietRide.Shared.Persistence.UnitTests.Outbox;

[CollectionDefinition(Name)]
public sealed class OutboxStoreCollection : ICollectionFixture<OutboxStoreFixture>
{
    public const string Name = "OutboxStore";
}
