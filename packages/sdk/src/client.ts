import type {
  AdjustStockInput,
  AdjustStockResult,
  ApproveOrderResult,
  AssignOrderToDeliveryRouteInput,
  AssignOrderToDeliveryRouteResult,
  AuthSession,
  CancelOrderResult,
  CancelStockTransferResult,
  CreateDeliveryRouteInput,
  CreateDeliveryRouteResult,
  ConfirmDeliveryInput,
  ConfirmDeliveryResult,
  CreateDeliveryProofUploadInput,
  CreateDeliveryProofUploadResult,
  CreateOrderInput,
  CreateOrderResult,
  CreateStockTransferInput,
  CreateStockTransferResult,
  DispatchDeliveryRouteInput,
  DispatchDeliveryRouteResult,
  ListDeliveryRouteStopsResult,
  LiquidateDeliveryRouteInput,
  LiquidateDeliveryRouteResult,
  ListDeliveryRouteCandidatesQuery,
  ListDeliveryRouteCandidatesResult,
  ListDeliveryRoutesQuery,
  ListDeliveryRoutesResult,
  ListOrdersQuery,
  ListOrdersResult,
  ListStockItemsQuery,
  ListStockItemsResult,
  ListPickingTasksQuery,
  ListPickingTasksResult,
  ListStockTransfersQuery,
  ListStockTransfersResult,
  CompletePickingTaskResult,
  OpenSalesSessionInput,
  OpenSalesSessionResult,
  GetCurrentSalesSessionQuery,
  GetCurrentSalesSessionResult,
  CreateSaleInput,
  CreateSaleResult,
  CloseSalesSessionInput,
  CloseSalesSessionResult,
  SalesSessionSummary,
  OrderDetail,
  OrderId,
  ReceiveStockTransferResult,
  ReportFailedDeliveryInput,
  ReportFailedDeliveryResult,
  RequeueFailedDeliveryOrderInput,
  RequeueFailedDeliveryOrderResult,
} from '@binexus/types';

import { BinexusApiError } from './errors';

export interface TokenProvider {
  getAccessToken(): string | null | Promise<string | null>;
  setTokens?(accessToken: string, refreshToken: string): void | Promise<void>;
  clear?(): void | Promise<void>;
}

export interface BinexusClientOptions {
  baseUrl: string;
  tokenProvider?: TokenProvider;
  fetch?: typeof fetch;
}

export interface LoginInput {
  email: string;
  password: string;
  tenantSlug: string;
}

export interface LoginResult {
  accessToken: string;
  refreshToken: string;
}

export interface CancelOrderInput {
  reason?: string;
}

const SLASH_CHAR_CODE = 47;

// Linear, regex-free trailing-slash stripping. Avoids the ReDoS surface (see
// CodeQL js/polynomial-redos) that a greedy anchored pattern like `/\/+$/`
// would expose when `baseUrl` is library input.
function stripTrailingSlashes(value: string): string {
  let end = value.length;
  while (end > 0 && value.charCodeAt(end - 1) === SLASH_CHAR_CODE) {
    end--;
  }
  return end === value.length ? value : value.slice(0, end);
}

export class BinexusClient {
  private readonly baseUrl: string;
  private readonly tokenProvider: TokenProvider | undefined;
  private readonly fetchImpl: typeof fetch;

  constructor(options: BinexusClientOptions) {
    this.baseUrl = stripTrailingSlashes(options.baseUrl);
    this.tokenProvider = options.tokenProvider;
    this.fetchImpl = options.fetch ?? fetch.bind(globalThis);
  }

  async login(input: LoginInput): Promise<LoginResult> {
    const result = await this.request<LoginResult>('POST', '/auth/login', {
      body: input,
      auth: false,
    });
    await this.tokenProvider?.setTokens?.(result.accessToken, result.refreshToken);
    return result;
  }

  async logout(): Promise<void> {
    await this.request<void>('POST', '/auth/logout', { auth: true }).catch(() => undefined);
    await this.tokenProvider?.clear?.();
  }

  async me(): Promise<AuthSession> {
    return this.request<AuthSession>('GET', '/auth/me', { auth: true });
  }

  async listOrders(query: ListOrdersQuery = {}): Promise<ListOrdersResult> {
    const params = new URLSearchParams();
    if (query.limit !== undefined) params.set('limit', String(query.limit));
    if (query.cursor) params.set('cursor', query.cursor);
    const qs = params.toString();
    const path = qs ? `/orders?${qs}` : '/orders';
    return this.request<ListOrdersResult>('GET', path, { auth: true });
  }

  async getOrder(id: OrderId | string): Promise<OrderDetail> {
    return this.request<OrderDetail>('GET', `/orders/${encodeURIComponent(id)}`, { auth: true });
  }

  async createOrder(input: CreateOrderInput): Promise<CreateOrderResult> {
    return this.request<CreateOrderResult>('POST', '/orders', { body: input, auth: true });
  }

  async approveOrder(id: OrderId | string): Promise<ApproveOrderResult> {
    return this.request<ApproveOrderResult>('POST', `/orders/${encodeURIComponent(id)}/approve`, {
      auth: true,
    });
  }

  async cancelOrder(
    id: OrderId | string,
    input: CancelOrderInput = {},
  ): Promise<CancelOrderResult> {
    return this.request<CancelOrderResult>('POST', `/orders/${encodeURIComponent(id)}/cancel`, {
      body: input,
      auth: true,
    });
  }

  async requeueFailedDeliveryOrder(
    id: OrderId | string,
    input: RequeueFailedDeliveryOrderInput = {},
  ): Promise<RequeueFailedDeliveryOrderResult> {
    return this.request<RequeueFailedDeliveryOrderResult>(
      'POST',
      `/orders/${encodeURIComponent(id)}/requeue-for-delivery`,
      { body: input, auth: true },
    );
  }

  async adjustStock(input: AdjustStockInput): Promise<AdjustStockResult> {
    return this.request<AdjustStockResult>('POST', '/inventory/stock/adjust', {
      body: input,
      auth: true,
    });
  }

  async createStockTransfer(input: CreateStockTransferInput): Promise<CreateStockTransferResult> {
    return this.request<CreateStockTransferResult>('POST', '/inventory/stock/transfers', {
      body: input,
      auth: true,
    });
  }

  async receiveStockTransfer(id: string): Promise<ReceiveStockTransferResult> {
    return this.request<ReceiveStockTransferResult>(
      'POST',
      `/inventory/stock/transfers/${encodeURIComponent(id)}/receive`,
      { auth: true },
    );
  }

  async cancelStockTransfer(id: string): Promise<CancelStockTransferResult> {
    return this.request<CancelStockTransferResult>(
      'POST',
      `/inventory/stock/transfers/${encodeURIComponent(id)}/cancel`,
      { auth: true },
    );
  }

  async listStockTransfers(query: ListStockTransfersQuery = {}): Promise<ListStockTransfersResult> {
    const params = new URLSearchParams();
    if (query.status) params.set('status', query.status);
    if (query.limit !== undefined) params.set('limit', String(query.limit));
    if (query.cursor) params.set('cursor', query.cursor);
    const qs = params.toString();
    const path = qs ? `/inventory/stock/transfers?${qs}` : '/inventory/stock/transfers';
    return this.request<ListStockTransfersResult>('GET', path, { auth: true });
  }

  async listPickingTasks(query: ListPickingTasksQuery = {}): Promise<ListPickingTasksResult> {
    const params = new URLSearchParams();
    if (query.status) params.set('status', query.status);
    if (query.limit !== undefined) params.set('limit', String(query.limit));
    if (query.cursor) params.set('cursor', query.cursor);
    const qs = params.toString();
    const path = qs ? `/warehouse/picking-tasks?${qs}` : '/warehouse/picking-tasks';
    return this.request<ListPickingTasksResult>('GET', path, { auth: true });
  }

  async completePickingTask(id: string): Promise<CompletePickingTaskResult> {
    return this.request<CompletePickingTaskResult>(
      'POST',
      `/warehouse/picking-tasks/${encodeURIComponent(id)}/complete`,
      { auth: true },
    );
  }

  async listDeliveryRouteCandidates(
    query: ListDeliveryRouteCandidatesQuery = {},
  ): Promise<ListDeliveryRouteCandidatesResult> {
    const params = new URLSearchParams();
    if (query.status) params.set('status', query.status);
    if (query.branchId) params.set('branchId', query.branchId);
    if (query.limit !== undefined) params.set('limit', String(query.limit));
    if (query.cursor) params.set('cursor', query.cursor);
    const qs = params.toString();
    const path = qs
      ? `/logistics/delivery-route-candidates?${qs}`
      : '/logistics/delivery-route-candidates';
    return this.request<ListDeliveryRouteCandidatesResult>('GET', path, { auth: true });
  }

  async listDeliveryRoutes(query: ListDeliveryRoutesQuery = {}): Promise<ListDeliveryRoutesResult> {
    const params = new URLSearchParams();
    if (query.status) params.set('status', query.status);
    if (query.branchId) params.set('branchId', query.branchId);
    if (query.limit !== undefined) params.set('limit', String(query.limit));
    if (query.cursor) params.set('cursor', query.cursor);
    const qs = params.toString();
    const path = qs ? `/logistics/delivery-routes?${qs}` : '/logistics/delivery-routes';
    return this.request<ListDeliveryRoutesResult>('GET', path, { auth: true });
  }

  async createDeliveryRoute(input: CreateDeliveryRouteInput): Promise<CreateDeliveryRouteResult> {
    return this.request<CreateDeliveryRouteResult>('POST', '/logistics/delivery-routes', {
      body: input,
      auth: true,
    });
  }

  async assignOrderToDeliveryRoute(
    deliveryRouteId: string,
    input: AssignOrderToDeliveryRouteInput,
  ): Promise<AssignOrderToDeliveryRouteResult> {
    return this.request<AssignOrderToDeliveryRouteResult>(
      'POST',
      `/logistics/delivery-routes/${encodeURIComponent(deliveryRouteId)}/assign-orders`,
      { body: input, auth: true },
    );
  }

  async dispatchDeliveryRoute(
    deliveryRouteId: string,
    input: DispatchDeliveryRouteInput = {},
  ): Promise<DispatchDeliveryRouteResult> {
    return this.request<DispatchDeliveryRouteResult>(
      'POST',
      `/logistics/delivery-routes/${encodeURIComponent(deliveryRouteId)}/dispatch`,
      { body: input, auth: true },
    );
  }

  async listDeliveryRouteStops(deliveryRouteId: string): Promise<ListDeliveryRouteStopsResult> {
    return this.request<ListDeliveryRouteStopsResult>(
      'GET',
      `/logistics/delivery-routes/${encodeURIComponent(deliveryRouteId)}/stops`,
      { auth: true },
    );
  }

  async confirmDelivery(
    deliveryRouteStopId: string,
    input: ConfirmDeliveryInput = {},
  ): Promise<ConfirmDeliveryResult> {
    return this.request<ConfirmDeliveryResult>(
      'POST',
      `/logistics/delivery-route-stops/${encodeURIComponent(deliveryRouteStopId)}/confirm-delivery`,
      { body: input, auth: true },
    );
  }

  async reportFailedDelivery(
    deliveryRouteStopId: string,
    input: ReportFailedDeliveryInput,
  ): Promise<ReportFailedDeliveryResult> {
    return this.request<ReportFailedDeliveryResult>(
      'POST',
      `/logistics/delivery-route-stops/${encodeURIComponent(deliveryRouteStopId)}/report-failed-delivery`,
      { body: input, auth: true },
    );
  }

  async createDeliveryProofUpload(
    deliveryRouteStopId: string,
    input: CreateDeliveryProofUploadInput,
  ): Promise<CreateDeliveryProofUploadResult> {
    return this.request<CreateDeliveryProofUploadResult>(
      'POST',
      `/logistics/delivery-route-stops/${encodeURIComponent(deliveryRouteStopId)}/proof-uploads`,
      { body: input, auth: true },
    );
  }

  async liquidateDeliveryRoute(
    deliveryRouteId: string,
    input: LiquidateDeliveryRouteInput,
  ): Promise<LiquidateDeliveryRouteResult> {
    return this.request<LiquidateDeliveryRouteResult>(
      'POST',
      `/logistics/delivery-routes/${encodeURIComponent(deliveryRouteId)}/liquidate`,
      { body: input, auth: true },
    );
  }

  async listStockItems(query: ListStockItemsQuery = {}): Promise<ListStockItemsResult> {
    const params = new URLSearchParams();
    if (query.branchId) params.set('branchId', query.branchId);
    if (query.productId) params.set('productId', query.productId);
    if (query.limit !== undefined) params.set('limit', String(query.limit));
    if (query.cursor) params.set('cursor', query.cursor);
    const qs = params.toString();
    const path = qs ? `/inventory/stock?${qs}` : '/inventory/stock';
    return this.request<ListStockItemsResult>('GET', path, { auth: true });
  }

  async openSalesSession(input: OpenSalesSessionInput): Promise<OpenSalesSessionResult> {
    return this.request<OpenSalesSessionResult>('POST', '/sales/sessions/open', {
      body: input,
      auth: true,
    });
  }

  async getCurrentSalesSession(
    query: GetCurrentSalesSessionQuery,
  ): Promise<GetCurrentSalesSessionResult> {
    const params = new URLSearchParams();
    params.set('terminalId', query.terminalId);
    if (query.branchId) params.set('branchId', query.branchId);
    return this.request<GetCurrentSalesSessionResult>(
      'GET',
      `/sales/sessions/current?${params.toString()}`,
      { auth: true },
    );
  }

  async getSalesSession(id: string): Promise<SalesSessionSummary> {
    return this.request<SalesSessionSummary>('GET', `/sales/sessions/${encodeURIComponent(id)}`, {
      auth: true,
    });
  }

  async createSale(sessionId: string, input: CreateSaleInput): Promise<CreateSaleResult> {
    return this.request<CreateSaleResult>(
      'POST',
      `/sales/sessions/${encodeURIComponent(sessionId)}/sales`,
      { body: input, auth: true },
    );
  }

  async closeSalesSession(
    sessionId: string,
    input: CloseSalesSessionInput,
  ): Promise<CloseSalesSessionResult> {
    return this.request<CloseSalesSessionResult>(
      'POST',
      `/sales/sessions/${encodeURIComponent(sessionId)}/close`,
      { body: input, auth: true },
    );
  }

  private async request<T>(
    method: string,
    path: string,
    opts: { body?: unknown; auth?: boolean } = {},
  ): Promise<T> {
    const headers: Record<string, string> = {};

    if (opts.auth && this.tokenProvider) {
      const token = await this.tokenProvider.getAccessToken();
      if (token) headers.authorization = `Bearer ${token}`;
    }

    const init: RequestInit = { method, headers };
    if (opts.body !== undefined) {
      headers['content-type'] = 'application/json';
      init.body = JSON.stringify(opts.body);
    }

    const response = await this.fetchImpl(`${this.baseUrl}${path}`, init);

    if (!response.ok) {
      const errPayload = await response.json().catch(() => ({}) as Record<string, unknown>);
      const message =
        (typeof errPayload.message === 'string' ? errPayload.message : undefined) ??
        `Request failed: ${response.status}`;
      const code = typeof errPayload.code === 'string' ? errPayload.code : undefined;
      throw new BinexusApiError(message, response.status, code, errPayload);
    }

    if (response.status === 204) return undefined as T;
    return (await response.json()) as T;
  }
}

export function createBinexusClient(options: BinexusClientOptions): BinexusClient {
  return new BinexusClient(options);
}
