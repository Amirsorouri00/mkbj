export interface SellerSettings {
    legalName: string;
    uen: string;
    address: string;
    email: string;
    gstRegistered: boolean;
    invoiceFooter: string;
    requiredBuyerFields: string[];
}

export interface PaymentInstructionVersion {
    id: string;
    version: number;
    text: string;
    isActive: boolean;
    createdBy: string | null;
    createdAt: string;
}

export interface CommerceLimitsSettings {
    minUsd: string;
    maxUsd: string;
    bulkDefaultMax: number;
    compositionDeadlineMs: number;
    compositionMaxLines: number;
}

export const moneyPattern = /^\d+\.\d{2}$/;

export function validateSeller(settings: SellerSettings): Record<string, string> {
    const errors: Record<string, string> = {};
    if (!settings.legalName.trim()) errors.legalName = 'Enter the legal seller name.';
    if (!settings.uen.trim()) errors.uen = 'Enter the Singapore UEN.';
    if (!settings.address.trim()) errors.address = 'Enter the invoice address.';
    if (!/^\S+@\S+\.\S+$/.test(settings.email.trim())) errors.email = 'Enter a valid email address.';
    if (settings.invoiceFooter.length > 1000) errors.invoiceFooter = 'Use 1,000 characters or fewer.';
    if (!settings.requiredBuyerFields.length) errors.requiredBuyerFields = 'Keep at least one required buyer field.';
    return errors;
}

export function validateLimits(settings: CommerceLimitsSettings): Record<string, string> {
    const errors: Record<string, string> = {};
    if (!moneyPattern.test(settings.minUsd)) errors.minUsd = 'Use exactly two decimals, for example 25.00.';
    if (!moneyPattern.test(settings.maxUsd)) errors.maxUsd = 'Use exactly two decimals, for example 100000.00.';
    if (moneyPattern.test(settings.minUsd) && moneyPattern.test(settings.maxUsd) && Number(settings.maxUsd) <= Number(settings.minUsd))
        errors.maxUsd = 'Maximum must be greater than minimum.';
    if (settings.bulkDefaultMax < 1 || settings.bulkDefaultMax > 1000) errors.bulkDefaultMax = 'Use a value from 1 to 1,000.';
    if (settings.compositionDeadlineMs < 1000 || settings.compositionDeadlineMs > 9500) errors.compositionDeadlineMs = 'Use 1,000–9,500 milliseconds.';
    if (settings.compositionMaxLines < 1 || settings.compositionMaxLines > 1000) errors.compositionMaxLines = 'Use a value from 1 to 1,000.';
    return errors;
}

export function parseBuyerFields(value: string): string[] {
    return [...new Set(value.split(/[\n,]/).map((item) => item.trim()).filter(Boolean))];
}
