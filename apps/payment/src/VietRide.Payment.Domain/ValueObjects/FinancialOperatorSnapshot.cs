namespace VietRide.Payment.Domain.ValueObjects;

public sealed record FinancialOperatorSnapshot
{
    public FinancialOperatorSnapshot(Guid operatorId, string name, string? logoUrl, string? contactPhone)
    {
        if (operatorId == Guid.Empty)
            throw new ArgumentException("Operator id is required.", nameof(operatorId));
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        OperatorId = operatorId;
        Name = name.Trim();
        LogoUrl = NormalizeOptional(logoUrl);
        ContactPhone = NormalizeOptional(contactPhone);
    }

    public Guid OperatorId { get; }
    public string Name { get; }
    public string? LogoUrl { get; }
    public string? ContactPhone { get; }

    private static string? NormalizeOptional(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
