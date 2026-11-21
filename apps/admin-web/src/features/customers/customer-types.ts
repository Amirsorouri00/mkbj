export interface OffsetPage<T> {
    page: number;
    pageSize: number;
    pageCount: number;
    totalCount: number;
    items: T[];
}

export interface CustomerRow {
    id: string;
    kind: string;
    partnerId: string | null;
    externalCustomerId: string | null;
    email: string | null;
    phone: string | null;
    fullName: string | null;
    companyName: string | null;
    status: string;
    createdAt: string;
}

export interface ContactChange {
    field: string;
    oldValue: string | null;
    newValue: string | null;
    changedAt: string;
    sourcePartnerId: string | null;
    correlationId: string;
}

export interface CustomerDetail {
    customer: CustomerRow;
    emailVerified: boolean;
    billingAddress: {
        line1: string;
        line2?: string | null;
        city: string;
        postalCode: string;
        countryIso2: string;
    } | null;
    registrationNumber: string | null;
    identityUserId: string | null;
    contactHistory: ContactChange[];
}

export interface Customer360 {
    customer: CustomerRow & Omit<CustomerDetail, 'customer'>;
    recentOrders: Array<{
        id: string;
        number: string;
        channel: string;
        currency: string;
        totalMinor: number;
        createdAt: string;
        status: { order: string; payment: string };
    }>;
}
