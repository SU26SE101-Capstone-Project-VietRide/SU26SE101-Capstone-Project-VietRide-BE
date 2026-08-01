namespace VietRide.Payment.Domain.Entities;

public sealed class DeletedFinancialActorMarker
{
    private DeletedFinancialActorMarker() { }

    public Guid UserId { get; private set; }
    public DateTimeOffset DeletedAt { get; private set; }
}
