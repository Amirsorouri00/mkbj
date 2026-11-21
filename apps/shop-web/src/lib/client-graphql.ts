'use client';

import type { TypedDocumentNode } from '@graphql-typed-document-node/core';
import { print } from 'graphql';
import { publicConfig } from './config';
import { ShopApiError } from './shop-api-error';

interface GraphQlEnvelope<T> {
  data?: T;
  errors?: Array<{ message?: string; extensions?: { code?: string; correlationId?: string } }>;
}

export async function shopClientRequest<TData, TVariables>(
  document: TypedDocumentNode<TData, TVariables>,
  variables: TVariables,
  token: string,
  signal?: AbortSignal,
): Promise<TData> {
  const response = await fetch(publicConfig.graphqlUrl, {
    method: 'POST',
    headers: {
      Accept: 'application/json',
      Authorization: `Bearer ${token}`,
      'Content-Type': 'application/json',
    },
    body: JSON.stringify({ query: print(document), variables }),
    ...(signal ? { signal } : {}),
  });

  let envelope: GraphQlEnvelope<TData>;
  try {
    envelope = (await response.json()) as GraphQlEnvelope<TData>;
  } catch {
    throw new ShopApiError('The shop service returned an unreadable response.', 'INVALID_RESPONSE');
  }

  const error = envelope.errors?.[0];
  if (error) {
    throw new ShopApiError(
      error.message ?? 'The request could not be completed.',
      error.extensions?.code ?? (response.status === 401 ? 'UNAUTHENTICATED' : 'GRAPHQL_ERROR'),
      error.extensions?.correlationId,
    );
  }
  if (!response.ok || !envelope.data) throw new ShopApiError('The request could not be completed.');
  return envelope.data;
}
