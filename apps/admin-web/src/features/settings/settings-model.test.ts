import { describe, expect, it } from 'vitest';
import { parseBuyerFields, validateLimits, validateSeller } from './settings-model';

describe('settings forms', () => {
    it('normalizes and de-duplicates required buyer fields', () => {
        expect(parseBuyerFields('companyName, email\ncompanyName')).toEqual(['companyName', 'email']);
    });

    it('enforces the backend commerce-limit contract before saving', () => {
        expect(validateLimits({ minUsd: '25', maxUsd: '20.00', bulkDefaultMax: 0, compositionDeadlineMs: 999, compositionMaxLines: 1001 })).toEqual({
            minUsd: 'Use exactly two decimals, for example 25.00.',
            bulkDefaultMax: 'Use a value from 1 to 1,000.',
            compositionDeadlineMs: 'Use 1,000–9,500 milliseconds.',
            compositionMaxLines: 'Use a value from 1 to 1,000.',
        });
    });

    it('requires invoice identity and valid contact details', () => {
        expect(Object.keys(validateSeller({ legalName: '', uen: '', address: '', email: 'bad', gstRegistered: false, invoiceFooter: '', requiredBuyerFields: [] }))).toEqual([
            'legalName', 'uen', 'address', 'email', 'requiredBuyerFields',
        ]);
    });
});
