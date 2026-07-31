namespace VietRide.Payment.Application.Abstractions.ExternalClients;

public interface IIdentityFinancialProjectionClient
{
    Task<IReadOnlyList<IdentityFinancialOperator>> GetOperatorsAsync(
        IReadOnlyList<Guid> operatorIds,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<IdentityFinancialUser>> GetUsersAsync(
        IReadOnlyList<Guid> userIds,
        CancellationToken cancellationToken = default);
}
