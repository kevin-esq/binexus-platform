namespace Binexus.Platform.Features.Contracts;

/// <summary>
/// Commercial feature keys — mirrors <c>packages/types/src/features.ts</c>.
/// </summary>
public enum FeatureKey
{
    PosRetail,
    PosRestaurant,
    Orders,
    Inventory,
    WarehouseLite,
    Routes,
    Liquidation,
    Billing,
    Analytics,
}

/// <summary>
/// Wire / DB string values for <see cref="FeatureKey"/> (SCREAMING_SNAKE).
/// </summary>
public static class FeatureKeyValues
{
    public const string PosRetail = "POS_RETAIL";
    public const string PosRestaurant = "POS_RESTAURANT";
    public const string Orders = "ORDERS";
    public const string Inventory = "INVENTORY";
    public const string WarehouseLite = "WAREHOUSE_LITE";
    public const string Routes = "ROUTES";
    public const string Liquidation = "LIQUIDATION";
    public const string Billing = "BILLING";
    public const string Analytics = "ANALYTICS";

    public static IReadOnlyList<string> All { get; } =
    [
        PosRetail,
        PosRestaurant,
        Orders,
        Inventory,
        WarehouseLite,
        Routes,
        Liquidation,
        Billing,
        Analytics,
    ];

    public static string ToWire(FeatureKey feature) => feature switch
    {
        FeatureKey.PosRetail => PosRetail,
        FeatureKey.PosRestaurant => PosRestaurant,
        FeatureKey.Orders => Orders,
        FeatureKey.Inventory => Inventory,
        FeatureKey.WarehouseLite => WarehouseLite,
        FeatureKey.Routes => Routes,
        FeatureKey.Liquidation => Liquidation,
        FeatureKey.Billing => Billing,
        FeatureKey.Analytics => Analytics,
        _ => throw new ArgumentOutOfRangeException(nameof(feature), feature, null),
    };

    public static FeatureKey Parse(string value)
    {
        var normalized = value?.Trim().ToUpperInvariant() ?? string.Empty;
        return normalized switch
        {
            PosRetail => FeatureKey.PosRetail,
            PosRestaurant => FeatureKey.PosRestaurant,
            Orders => FeatureKey.Orders,
            Inventory => FeatureKey.Inventory,
            WarehouseLite => FeatureKey.WarehouseLite,
            Routes => FeatureKey.Routes,
            Liquidation => FeatureKey.Liquidation,
            Billing => FeatureKey.Billing,
            Analytics => FeatureKey.Analytics,
            _ => throw new ArgumentOutOfRangeException(nameof(value), value, "Unknown feature key."),
        };
    }
}

/// <summary>
/// Cross-module port for DB-backed tenant commercial entitlements (ADR-0009).
/// Callers pass an explicit tenant id; HTTP handlers must use the JWT tenant.
/// Identity persists <c>tenant_features</c> rows but does not own this commercial API.
/// </summary>
public interface ITenantFeatureService
{
    Task<bool> IsEnabledAsync(Guid tenantId, FeatureKey feature, CancellationToken cancellationToken = default);

    Task SetEnabledAsync(Guid tenantId, FeatureKey feature, bool enabled, CancellationToken cancellationToken = default);
}
