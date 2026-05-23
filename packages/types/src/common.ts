export type ISODateString = string;

export type Branded<T, B> = T & { readonly __brand: B };

export type UserId = Branded<string, 'UserId'>;
export type TenantId = Branded<string, 'TenantId'>;
export type BranchId = Branded<string, 'BranchId'>;
export type OrderId = Branded<string, 'OrderId'>;

export interface Paginated<T> {
  items: T[];
  total: number;
  page: number;
  pageSize: number;
}

export interface Result<T, E = string> {
  ok: boolean;
  value?: T;
  error?: E;
}
