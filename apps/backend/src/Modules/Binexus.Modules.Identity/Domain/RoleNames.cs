namespace Binexus.Modules.Identity.Domain;

public static class RoleNames
{
    public const string SuperAdmin = "SUPER_ADMIN";
    public const string Admin = "ADMIN";
    public const string Cashier = "CASHIER";
    public const string Warehouse = "WAREHOUSE";
    public const string Driver = "DRIVER";

    public static IReadOnlySet<string> All { get; } = new HashSet<string>(
        [SuperAdmin, Admin, Cashier, Warehouse, Driver],
        StringComparer.Ordinal);

    public static bool IsKnown(string? role) =>
        !string.IsNullOrWhiteSpace(role) && All.Contains(role);
}
