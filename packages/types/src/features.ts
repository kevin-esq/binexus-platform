// Feature flag registry — central source of truth for feature keys.
// Each tenant has rows in TenantFeature for the keys they have opted into.

export const FeatureKey = {
  POS_RETAIL: 'POS_RETAIL',
  POS_RESTAURANT: 'POS_RESTAURANT',
  ORDERS: 'ORDERS',
  INVENTORY: 'INVENTORY',
  WAREHOUSE_LITE: 'WAREHOUSE_LITE',
  ROUTES: 'ROUTES',
  LIQUIDATION: 'LIQUIDATION',
  BILLING: 'BILLING',
  ANALYTICS: 'ANALYTICS',
} as const;

export type FeatureKey = (typeof FeatureKey)[keyof typeof FeatureKey];

export const ALL_FEATURE_KEYS: FeatureKey[] = Object.values(FeatureKey);

export interface TenantFeatureConfig {
  // Free-form per-feature configuration; each feature defines its own shape.
  // Kept as record so persistence in Prisma's Json column stays flexible.
  [key: string]: unknown;
}

export interface TenantFeature {
  tenantId: string;
  key: FeatureKey;
  enabled: boolean;
  config: TenantFeatureConfig | null;
}
