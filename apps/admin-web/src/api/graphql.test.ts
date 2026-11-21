import type { TypedDocumentNode } from '@graphql-typed-document-node/core';
import { parse } from 'graphql';
import { afterEach, describe, expect, it, vi } from 'vitest';
import { AppError } from './errors';
import { graphQlRequest } from './graphql';

interface TestData { catalogItem: { id: string } }
interface TestVariables { id: string }
const document = parse('query CatalogItem($id: ID!) { catalogItem(id: $id) { id } }') as unknown as TypedDocumentNode<TestData, TestVariables>;

afterEach(() => vi.unstubAllGlobals());

describe('GraphQL transport', () => {
  it('sends a generated document, opaque ID, bearer token, and cancellation signal', async () => {
    const fetchMock = vi.fn().mockResolvedValue(new Response(JSON.stringify({ data: { catalogItem: { id: '9007199254740993' } } }), { status: 200, headers: { 'Content-Type': 'application/json' } }));
    vi.stubGlobal('fetch', fetchMock);
    const controller = new AbortController();

    await expect(graphQlRequest(document, { id: '9007199254740993' }, 'staff-token', controller.signal)).resolves.toEqual({ catalogItem: { id: '9007199254740993' } });

    const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
    expect(url).toBe('http://localhost:8608/graphql');
    expect(init.signal).toBe(controller.signal);
    expect(init.headers).toMatchObject({ Authorization: 'Bearer staff-token', 'Content-Type': 'application/json' });
    if (typeof init.body !== 'string') throw new Error('Expected a JSON request body.');
    expect(JSON.parse(init.body)).toMatchObject({ variables: { id: '9007199254740993' } });
  });

  it('normalizes a GraphQL error envelope without losing correlation or retry metadata', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response(JSON.stringify({ errors: [{ message: 'Product changed.', extensions: { code: 'CONFLICT', status: 409, correlationId: 'corr-42', retryable: true } }] }), { status: 200 })));

    const error = await graphQlRequest(document, { id: '1' }, 'staff-token').catch((caught: unknown) => caught);

    expect(error).toBeInstanceOf(AppError);
    expect(error).toMatchObject({ message: 'Product changed.', code: 'CONFLICT', status: 409, correlationId: 'corr-42', retryable: true });
  });

  it.each([
    [401, 'unauthorized', 'Your session has expired. Sign in again.'],
    [403, 'forbidden', 'You do not have permission to view this area.'],
  ])('maps HTTP %s before attempting to parse the response', async (status, code, message) => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('', { status })));

    await expect(graphQlRequest(document, { id: '1' }, 'staff-token')).rejects.toMatchObject({ code, status, message });
  });

  it('rejects unreadable successful responses instead of returning partial data', async () => {
    vi.stubGlobal('fetch', vi.fn().mockResolvedValue(new Response('not-json', { status: 200 })));

    await expect(graphQlRequest(document, { id: '1' }, 'staff-token')).rejects.toMatchObject({ code: 'invalid_response', status: 200 });
  });
});
