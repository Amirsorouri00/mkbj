import { appConfig } from '../config';
import { AppError } from './errors';

interface RestErrorBody {
    code?: string;
    message?: string;
    retryable?: boolean;
    correlationId?: string;
}

async function restError(response: Response): Promise<AppError> {
    let body: RestErrorBody = {};
    try {
        body = (await response.json()) as RestErrorBody;
    } catch {
        // Some infrastructure failures have no JSON body; the status remains useful.
    }
    return new AppError(
        body.message || 'The request could not be completed.',
        body.code || 'request_failed',
        response.status,
        body.correlationId || response.headers.get('X-Correlation-ID') || undefined,
        body.retryable ?? false,
    );
}

interface RestSuccessBody<T> {
    data?: T;
    message?: string;
}

export async function restRequest<T>(
    path: string,
    token: string,
    init: RequestInit = {},
    signal?: AbortSignal,
): Promise<T> {
    const headers = new Headers(init.headers);
    headers.set('Authorization', `Bearer ${token}`);
    if (init.body !== undefined && !(init.body instanceof FormData))
        headers.set('Content-Type', 'application/json');
    const response = await fetch(`${appConfig.apiBaseUrl}${path}`, {
        ...init,
        headers,
        ...(signal ? { signal } : {}),
    });
    if (!response.ok) throw await restError(response);
    if (response.status === 204 || response.headers.get('Content-Length') === '0')
        return undefined as T;
    const body = (await response.json()) as RestSuccessBody<T>;
    return body.data as T;
}

export function restGet<T>(path: string, token: string, signal?: AbortSignal): Promise<T> {
    return restRequest<T>(path, token, {}, signal);
}

export function restSend<T = void>(
    path: string,
    token: string,
    method: 'POST' | 'PUT' | 'DELETE',
    body?: unknown,
    signal?: AbortSignal,
): Promise<T> {
    return restRequest<T>(
        path,
        token,
        { method, ...(body === undefined ? {} : { body: JSON.stringify(body) }) },
        signal,
    );
}

export async function fetchAuthenticatedDocument(path: string, token: string): Promise<string> {
    const response = await fetch(`${appConfig.apiBaseUrl}${path}`, {
        headers: { Authorization: `Bearer ${token}` },
    });
    if (!response.ok) throw await restError(response);
    return URL.createObjectURL(await response.blob());
}

export async function downloadAuthenticatedFile(
    path: string,
    token: string,
    fallbackFileName: string,
): Promise<void> {
    const response = await fetch(`${appConfig.apiBaseUrl}${path}`, {
        headers: { Authorization: `Bearer ${token}` },
    });
    if (!response.ok) throw await restError(response);

    const disposition = response.headers.get('Content-Disposition') ?? '';
    const encoded = disposition.match(/filename\*=UTF-8''([^;]+)/i)?.[1];
    const quoted = disposition.match(/filename="([^"]+)"/i)?.[1];
    const fileName = encoded ? decodeURIComponent(encoded) : quoted || fallbackFileName;
    const url = URL.createObjectURL(await response.blob());
    const link = document.createElement('a');
    link.href = url;
    link.download = fileName;
    document.body.append(link);
    link.click();
    link.remove();
    URL.revokeObjectURL(url);
}

export async function downloadAuthenticatedPdfData(
    path: string,
    token: string,
    fallbackFileName: string,
): Promise<void> {
    const file = await restGet<{ fileName: string; contentBase64: string }>(path, token);
    const binary = atob(file.contentBase64);
    const bytes = new Uint8Array(binary.length);
    for (let index = 0; index < binary.length; index += 1) bytes[index] = binary.charCodeAt(index);
    const url = URL.createObjectURL(new Blob([bytes], { type: 'application/pdf' }));
    const link = document.createElement('a');
    link.href = url;
    link.download = file.fileName || fallbackFileName;
    document.body.append(link);
    link.click();
    link.remove();
    URL.revokeObjectURL(url);
}

export interface ProductMediaUpload {
    file: File;
    alt: string;
    rights: 'Allowed' | 'RightsUnknown' | 'Denied';
    sortOrder?: number;
}

export async function uploadProductMedia(
    productId: string,
    input: ProductMediaUpload,
    token: string,
    signal?: AbortSignal,
): Promise<void> {
    const body = new FormData();
    body.set('file', input.file);
    if (input.alt.trim()) body.set('alt', input.alt.trim());
    body.set('rights', input.rights);
    if (input.sortOrder !== undefined) body.set('sortOrder', String(input.sortOrder));

    const response = await fetch(
        `${appConfig.apiBaseUrl}/api/v1/admin/catalog/products/${encodeURIComponent(productId)}/media`,
        {
            method: 'POST',
            headers: { Authorization: `Bearer ${token}` },
            body,
            ...(signal ? { signal } : {}),
        },
    );
    if (!response.ok) throw await restError(response);
}

export interface ImportFilePreview {
    columns: string[];
    sampleRows: Array<Record<string, string | null>>;
    sampleSize: number;
}

export async function uploadImportFile(
    jobId: string,
    file: File,
    token: string,
    signal?: AbortSignal,
): Promise<ImportFilePreview> {
    const body = new FormData();
    body.set('file', file);
    return restRequest<ImportFilePreview>(
        `/api/v1/admin/imports/jobs/${encodeURIComponent(jobId)}/file`,
        token,
        { method: 'POST', body },
        signal,
    );
}
