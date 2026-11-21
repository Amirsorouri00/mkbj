import type { TypedDocumentNode } from '@graphql-typed-document-node/core';
import { print } from 'graphql';
import { appConfig } from '../config';
import { AppError, normalizeGraphQlError } from './errors';

interface GraphQlEnvelope<TData> {
  data?: TData;
  errors?: Array<{
    message?: string;
    extensions?: {
      code?: string;
      status?: number;
      httpStatus?: number;
      correlationId?: string;
      retryable?: boolean;
    };
  }>;
}

export async function graphQlRequest<TData, TVariables>(
  document: TypedDocumentNode<TData, TVariables>,
  variables: TVariables,
  token: string,
  signal?: AbortSignal,
): Promise<TData> {
  const response = await fetch(appConfig.graphqlUrl, {
    method: 'POST',
    headers: {
      Accept: 'application/json',
      Authorization: `Bearer ${token}`,
      'Content-Type': 'application/json',
    },
    body: JSON.stringify({ query: print(document), variables }),
    ...(signal ? { signal } : {}),
  });

  if (response.status === 401) {
    throw new AppError('Your session has expired. Sign in again.', 'unauthorized', 401);
  }

  if (response.status === 403) {
    throw new AppError('You do not have permission to view this area.', 'forbidden', 403);
  }

  let envelope: GraphQlEnvelope<TData>;
  try {
    envelope = (await response.json()) as GraphQlEnvelope<TData>;
  } catch {
    throw new AppError('The server returned an unreadable response.', 'invalid_response', response.status);
  }

  const firstError = envelope.errors?.[0];
  if (firstError) throw normalizeGraphQlError(firstError, response.status);
  if (!response.ok || !envelope.data) {
    throw new AppError('The request could not be completed.', 'request_failed', response.status);
  }

  return envelope.data;
}
