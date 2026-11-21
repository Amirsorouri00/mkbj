import { afterEach, describe, expect, it, vi } from 'vitest';
import { AppError } from './errors';
import { restGet, restSend, uploadImportFile, uploadProductMedia } from './rest';

afterEach(() => vi.unstubAllGlobals());

describe('REST media transport', () => {
    it('encodes the product ID and sends multipart fields without a browser-visible storage secret', async () => {
        const fetchMock = vi.fn().mockResolvedValue(new Response(null, { status: 204 }));
        vi.stubGlobal('fetch', fetchMock);
        const file = new File(['image'], 'chair.png', { type: 'image/png' });

        await uploadProductMedia(
            'product/9007199254740993',
            { file, alt: ' Orange chair ', rights: 'Allowed', sortOrder: 3 },
            'staff-token',
        );

        const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
        expect(url).toBe(
            'http://localhost:8608/api/v1/admin/catalog/products/product%2F9007199254740993/media',
        );
        expect(init.headers).toEqual({ Authorization: 'Bearer staff-token' });
        const body = init.body as FormData;
        expect(body.get('file')).toBe(file);
        expect(body.get('alt')).toBe('Orange chair');
        expect(body.get('rights')).toBe('Allowed');
        expect(body.get('sortOrder')).toBe('3');
    });

    it('preserves the endpoint error envelope and retry guidance', async () => {
        vi.stubGlobal(
            'fetch',
            vi.fn().mockResolvedValue(
                new Response(
                    JSON.stringify({
                        code: 'MEDIA_TOO_LARGE',
                        message: 'Use a smaller file.',
                        retryable: false,
                        correlationId: 'corr-media',
                    }),
                    { status: 413, headers: { 'Content-Type': 'application/json' } },
                ),
            ),
        );

        const error = await uploadProductMedia(
            '1',
            { file: new File(['x'], 'large.png'), alt: '', rights: 'RightsUnknown' },
            'staff-token',
        ).catch((caught: unknown) => caught);

        expect(error).toBeInstanceOf(AppError);
        expect(error).toMatchObject({
            code: 'MEDIA_TOO_LARGE',
            message: 'Use a smaller file.',
            status: 413,
            correlationId: 'corr-media',
            retryable: false,
        });
    });

    it('falls back to the correlation response header for infrastructure errors', async () => {
        vi.stubGlobal(
            'fetch',
            vi.fn().mockResolvedValue(
                new Response('', {
                    status: 503,
                    headers: { 'X-Correlation-ID': 'corr-header' },
                }),
            ),
        );

        await expect(
            uploadProductMedia(
                '1',
                { file: new File(['x'], 'image.png'), alt: '', rights: 'Denied' },
                'staff-token',
            ),
        ).rejects.toMatchObject({
            code: 'request_failed',
            status: 503,
            correlationId: 'corr-header',
        });
    });
});

describe('REST JSON transport', () => {
    it('unwraps the shared success envelope and preserves query strings', async () => {
        vi.stubGlobal(
            'fetch',
            vi.fn().mockResolvedValue(
                new Response(JSON.stringify({ message: 'Success', data: { items: [1] } }), {
                    status: 200,
                    headers: { 'Content-Type': 'application/json' },
                }),
            ),
        );
        await expect(
            restGet<{ items: number[] }>('/api/v1/admin/customers?page=2', 'staff-token'),
        ).resolves.toEqual({ items: [1] });
        const call = vi.mocked(fetch).mock.calls[0];
        expect(call?.[0]).toBe('http://localhost:8608/api/v1/admin/customers?page=2');
        const headers = (call?.[1] as RequestInit).headers as Headers;
        expect(headers.get('Authorization')).toBe('Bearer staff-token');
    });

    it('sends JSON writes through the same error handling', async () => {
        vi.stubGlobal(
            'fetch',
            vi.fn().mockResolvedValue(
                new Response(JSON.stringify({ code: 'FORBIDDEN', message: 'Not allowed.' }), {
                    status: 403,
                    headers: { 'Content-Type': 'application/json' },
                }),
            ),
        );
        await expect(
            restSend('/api/v1/admin/customers/1/suspend', 'staff-token', 'POST'),
        ).rejects.toMatchObject({ status: 403, code: 'FORBIDDEN' });
    });
});

describe('REST import transport', () => {
    it('uploads a supplier file and unwraps the sample preview', async () => {
        const preview = {
            columns: ['Name', 'Price'],
            sampleRows: [{ Name: 'Kettle', Price: '12.00' }],
            sampleSize: 5,
        };
        const fetchMock = vi.fn().mockResolvedValue(
            new Response(JSON.stringify({ data: preview }), {
                status: 200,
                headers: { 'Content-Type': 'application/json' },
            }),
        );
        vi.stubGlobal('fetch', fetchMock);
        const file = new File(['Name,Price\nKettle,12.00'], 'supplier.csv', {
            type: 'text/csv',
        });

        await expect(uploadImportFile('job/42', file, 'staff-token')).resolves.toEqual(preview);

        const [url, init] = fetchMock.mock.calls[0] as [string, RequestInit];
        expect(url).toBe('http://localhost:8608/api/v1/admin/imports/jobs/job%2F42/file');
        expect((init.headers as Headers).get('Authorization')).toBe('Bearer staff-token');
        expect((init.body as FormData).get('file')).toBe(file);
        expect((init.headers as Headers).has('Content-Type')).toBe(false);
    });
});
