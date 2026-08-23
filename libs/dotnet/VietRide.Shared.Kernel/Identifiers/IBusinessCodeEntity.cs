namespace VietRide.Shared.Kernel.Identifiers;

public interface IBusinessCodeEntity
{
    string BusinessCodeConstraintName { get; }

    void RegenerateBusinessCode();
}
