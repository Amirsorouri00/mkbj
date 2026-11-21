import type { TypedDocumentNode } from '@graphql-typed-document-node/core';
import { print } from 'graphql';
import { serverConfig } from './config';
import { ShopApiError } from './shop-api-error';

export { ShopApiError } from './shop-api-error';

interface GraphQlEnvelope<T> {
  data?: T;
  errors?: Array<{ message?: string; extensions?: { code?: string; correlationId?: string } }>;
}

export async function shopQuery<TData, TVariables>(
  document: TypedDocumentNode<TData, TVariables>,
  variables: TVariables,
): Promise<TData> {
  let response: Response;
  try {
    response = await fetch(serverConfig.graphqlUrl, {
      method: 'POST',
      headers: { Accept: 'application/json', 'Content-Type': 'application/json' },
      body: JSON.stringify({ query: print(document), variables }),
      // These operations read the deliberately stale storefront projection. A short shared
      // cache keeps every server render from consuming the API's anonymous, per-IP allowance;
      // purchasing still goes through the uncached authenticated client and re-quotes live data.
      next: { revalidate: 30 },
    });
  } catch {
    throw new ShopApiError('The shop service is temporarily unavailable. Please try again shortly.', 'NETWORK_ERROR');
  }

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
      error.extensions?.code ?? 'GRAPHQL_ERROR',
      error.extensions?.correlationId,
    );
  }
  if (!response.ok || !envelope.data) throw new ShopApiError('The request could not be completed.');
  return envelope.data;
}
