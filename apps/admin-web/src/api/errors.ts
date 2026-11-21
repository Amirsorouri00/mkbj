export class AppError extends Error {
  constructor(
    message: string,
    public readonly code = 'unexpected_error',
    public readonly status?: number,
    public readonly correlationId?: string,
    public readonly retryable = false,
  ) {
    super(message);
    this.name = 'AppError';
  }
}

interface GraphQlErrorShape {
  message?: string;
  extensions?: {
    code?: string;
    status?: number;
    httpStatus?: number;
    correlationId?: string;
    retryable?: boolean;
  };
}

export function normalizeGraphQlError(error: GraphQlErrorShape, responseStatus?: number): AppError {
  return new AppError(
    error.message || 'The request could not be completed.',
    error.extensions?.code || 'graphql_error',
    error.extensions?.status || error.extensions?.httpStatus || responseStatus,
    error.extensions?.correlationId,
    error.extensions?.retryable ?? false,
  );
}

export function toAppError(error: unknown): AppError {
  if (error instanceof AppError) return error;
  if (error instanceof Error) return new AppError(error.message);
  return new AppError('An unexpected error occurred.');
}
