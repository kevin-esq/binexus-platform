namespace Binexus.Platform.Tenancy;

public sealed record TenantContext(
    Guid TenantId,
    Guid? UserId,
    string? Role,
    Guid? BranchId,
    string RequestId);

public interface ICurrentTenant
{
    TenantContext? Current { get; }

    void SetContext(TenantContext context);

    void Clear();
}

public sealed class CurrentTenant : ICurrentTenant
{
    private static readonly AsyncLocal<TenantContext?> Holder = new();

    public TenantContext? Current => Holder.Value;

    public void SetContext(TenantContext context) => Holder.Value = context;

    public void Clear() => Holder.Value = null;
}
