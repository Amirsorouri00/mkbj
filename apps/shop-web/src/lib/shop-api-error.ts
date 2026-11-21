export class ShopApiError extends Error {
  constructor(
    message: string,
    readonly code = 'SHOP_REQUEST_FAILED',
    readonly correlationId?: string,
  ) {
    super(message);
    this.name = 'ShopApiError';
  }
}
