import type { BranchId, ISODateString, TenantId, UserId } from './common';

export const Role = {
  SUPER_ADMIN: 'SUPER_ADMIN',
  ADMIN: 'ADMIN',
  CASHIER: 'CASHIER',
  WAREHOUSE: 'WAREHOUSE',
  DRIVER: 'DRIVER',
} as const;

export type Role = (typeof Role)[keyof typeof Role];

export const ALL_ROLES: Role[] = Object.values(Role);

export interface Tenant {
  id: TenantId;
  slug: string;
  name: string;
  createdAt: ISODateString;
}

export interface Branch {
  id: BranchId;
  tenantId: TenantId;
  name: string;
}

export interface User {
  id: UserId;
  tenantId: TenantId;
  email: string;
  role: Role;
  branchId: BranchId | null;
}

export interface AuthSession {
  user: User;
  tenant: Tenant;
  branch: Branch | null;
}

export interface JwtAccessClaims {
  sub: UserId;
  tenantId: TenantId;
  role: Role;
  branchId: BranchId | null;
  iat: number;
  exp: number;
}
