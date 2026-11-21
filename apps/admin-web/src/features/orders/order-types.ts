export interface OrderStatus {
    order: string;
    payment: string;
    paymentSource?: string | null;
    fulfillment: string;
    fulfillmentSource?: string | null;
    fulfillmentStatement: string;
    documents: string;
}
export interface OrderRow {
    id: string;
    number: string;
    channel: string;
    partnerId: string | null;
    externalOrderId: string | null;
    customerId: string;
    currency: string;
    totalMinor: number;
    refundedMinor: number;
    status: OrderStatus;
    createdAt: string;
}
export interface CursorPage<T> {
    items: T[];
    nextCursor: string | null;
}
export interface Order360 {
    order: {
        summary: OrderRow;
        externalCustomerId: string | null;
        usdEquivalentMinor: number;
        correlationId: string;
        lineCount: number;
        payments: Array<{
            id: string;
            source: string;
            method: string | null;
            reference: string | null;
            recordedAt: string;
            amountMinor: number;
        }>;
        documents: Array<{
            id: string;
            type: string;
            number: string;
            issuedAt: string;
            relatedDocumentId: string | null;
            pdfPath: string;
        }>;
        refunds: Array<{
            id: string;
            amountMinor: number;
            reason: string;
            recordedAt: string;
            externalReference: string | null;
            creditNoteDocumentId: string;
        }>;
    };
    customer: {
        id: string;
        fullName: string | null;
        companyName: string | null;
        email: string | null;
        status: string;
    } | null;
}
export function formatMinor(minor: number, currency: string): string {
    return new Intl.NumberFormat(undefined, { style: 'currency', currency }).format(minor / 100);
}
