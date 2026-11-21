import { Button } from '@mkbj/ui';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { Check, Clock3, FileText, Landmark } from 'lucide-react';
import { type FormEvent, useEffect, useState } from 'react';
import { restGet, restSend } from '../../api/rest';
import { toAppError } from '../../api/errors';
import { useAuth } from '../../auth/auth-context';
import { ErrorState, PageLoading } from '../../components/feedback';
import { StatusBadge } from '../../components/status-badge';
import {
    type CommerceLimitsSettings,
    parseBuyerFields,
    type PaymentInstructionVersion,
    type SellerSettings,
    validateLimits,
    validateSeller,
} from './settings-model';

const emptySeller: SellerSettings = {
    legalName: '', uen: '', address: '', email: '', gstRegistered: false, invoiceFooter: '', requiredBuyerFields: [],
};
const emptyLimits: CommerceLimitsSettings = {
    minUsd: '', maxUsd: '', bulkDefaultMax: 100, compositionDeadlineMs: 8000, compositionMaxLines: 200,
};

function FieldError({ id, message }: { id: string; message: string | undefined }) {
    return message ? <span className="field-error" id={id}>{message}</span> : null;
}

export function SettingsPage() {
    const { token, roles } = useAuth();
    const client = useQueryClient();
    const sellerQuery = useQuery({ queryKey: ['admin-settings', 'seller'], queryFn: ({ signal }) => restGet<SellerSettings>('/api/v1/admin/settings/seller', token!, signal), enabled: Boolean(token && roles.includes('super_admin')) });
    const instructionsQuery = useQuery({ queryKey: ['admin-settings', 'payment-instructions'], queryFn: ({ signal }) => restGet<PaymentInstructionVersion[]>('/api/v1/admin/settings/payment-instructions', token!, signal), enabled: Boolean(token && roles.includes('super_admin')) });
    const limitsQuery = useQuery({ queryKey: ['admin-settings', 'commerce-limits'], queryFn: ({ signal }) => restGet<CommerceLimitsSettings>('/api/v1/admin/settings/commerce-limits', token!, signal), enabled: Boolean(token && roles.includes('super_admin')) });
    const [seller, setSeller] = useState<SellerSettings>(emptySeller);
    const [buyerFields, setBuyerFields] = useState('');
    const [sellerErrors, setSellerErrors] = useState<Record<string, string>>({});
    const [sellerNotice, setSellerNotice] = useState<string | null>(null);
    const [limits, setLimits] = useState<CommerceLimitsSettings>(emptyLimits);
    const [limitErrors, setLimitErrors] = useState<Record<string, string>>({});
    const [limitsNotice, setLimitsNotice] = useState<string | null>(null);
    const [instructionText, setInstructionText] = useState('');
    const [instructionError, setInstructionError] = useState<string | null>(null);
    const [instructionNotice, setInstructionNotice] = useState<string | null>(null);

    useEffect(() => {
        if (!sellerQuery.data) return;
        setSeller(sellerQuery.data);
        setBuyerFields(sellerQuery.data.requiredBuyerFields.join('\n'));
    }, [sellerQuery.data]);
    useEffect(() => { if (limitsQuery.data) setLimits(limitsQuery.data); }, [limitsQuery.data]);

    const sellerSave = useMutation({
        mutationFn: (value: SellerSettings) => restSend('/api/v1/admin/settings/seller', token!, 'PUT', value),
        onSuccess: async () => { setSellerNotice('Seller identity saved. New invoices will use this profile.'); setSellerErrors({}); await client.invalidateQueries({ queryKey: ['admin-settings', 'seller'] }); },
        onError: (error) => setSellerErrors({ form: toAppError(error).message }),
    });
    const limitsSave = useMutation({
        mutationFn: (value: CommerceLimitsSettings) => restSend('/api/v1/admin/settings/commerce-limits', token!, 'PUT', value),
        onSuccess: async () => { setLimitsNotice('Commerce limits saved.'); setLimitErrors({}); await client.invalidateQueries({ queryKey: ['admin-settings', 'commerce-limits'] }); },
        onError: (error) => setLimitErrors({ form: toAppError(error).message }),
    });
    const publish = useMutation({
        mutationFn: () => restSend<number>('/api/v1/admin/settings/payment-instructions', token!, 'POST', { text: instructionText.trim() }),
        onSuccess: async (version) => { setInstructionText(''); setInstructionError(null); setInstructionNotice(`Payment instruction version ${version} published and activated.`); await client.invalidateQueries({ queryKey: ['admin-settings', 'payment-instructions'] }); },
        onError: (error) => setInstructionError(toAppError(error).message),
    });
    const activate = useMutation({
        mutationFn: (version: number) => restSend(`/api/v1/admin/settings/payment-instructions/${version}/activate`, token!, 'POST'),
        onSuccess: async () => { setInstructionError(null); setInstructionNotice('The selected payment instruction is active.'); await client.invalidateQueries({ queryKey: ['admin-settings', 'payment-instructions'] }); },
        onError: (error) => setInstructionError(toAppError(error).message),
    });

    if (!roles.includes('super_admin')) return <ErrorState error={new Error('Settings are restricted to Super Admin accounts.')} />;
    if (sellerQuery.isPending || instructionsQuery.isPending || limitsQuery.isPending) return <PageLoading label="Loading settings" />;
    const loadError = sellerQuery.error || instructionsQuery.error || limitsQuery.error;
    if (loadError) return <ErrorState error={loadError} retry={() => { void sellerQuery.refetch(); void instructionsQuery.refetch(); void limitsQuery.refetch(); }} />;
    const instructions = instructionsQuery.data ?? [];

    const submitSeller = (event: FormEvent) => {
        event.preventDefault();
        const value = { ...seller, requiredBuyerFields: parseBuyerFields(buyerFields) };
        const errors = validateSeller(value);
        setSellerErrors(errors); setSellerNotice(null);
        if (!Object.keys(errors).length) sellerSave.mutate(value);
    };
    const submitLimits = (event: FormEvent) => {
        event.preventDefault();
        const errors = validateLimits(limits);
        setLimitErrors(errors); setLimitsNotice(null);
        if (!Object.keys(errors).length) limitsSave.mutate(limits);
    };
    const submitInstruction = (event: FormEvent) => {
        event.preventDefault();
        setInstructionNotice(null);
        if (!instructionText.trim()) return setInstructionError('Enter the payment instructions customers should follow.');
        if (instructionText.length > 4000) return setInstructionError('Use 4,000 characters or fewer.');
        setInstructionError(null); publish.mutate();
    };

    return <section aria-labelledby="settings-title" className="page-stack settings-page">
        <div className="page-heading"><div><p className="eyebrow">System</p><h1 id="settings-title">Settings</h1><p>Manage the invoice identity, customer payment wording and safe operating limits used across MKBJ.</p></div><StatusBadge status="Super admin" /></div>

        <form className="panel form-section settings-section" onSubmit={submitSeller} noValidate>
            <div className="section-copy settings-section__heading"><span className="launch-icon"><Landmark aria-hidden="true" size={20} /></span><div><h2>Seller identity</h2><p>This legal block is copied into newly generated invoices.</p></div></div>
            {sellerErrors.form && <div className="dialog-error" role="alert">{sellerErrors.form}</div>}
            {sellerNotice && <div className="success-panel" role="status"><span>{sellerNotice}</span><Button onClick={() => setSellerNotice(null)} size="compact" type="button" variant="ghost">Dismiss</Button></div>}
            <div className="form-grid">
                <label className="field"><span>Legal name <b>*</b></span><input aria-describedby="legal-name-error" aria-invalid={Boolean(sellerErrors.legalName)} maxLength={200} onBlur={() => setSellerErrors(validateSeller({ ...seller, requiredBuyerFields: parseBuyerFields(buyerFields) }))} onChange={(event) => setSeller({ ...seller, legalName: event.target.value })} value={seller.legalName} /><FieldError id="legal-name-error" message={sellerErrors.legalName} /></label>
                <label className="field"><span>Singapore UEN <b>*</b></span><input aria-describedby="uen-error" aria-invalid={Boolean(sellerErrors.uen)} maxLength={32} onChange={(event) => setSeller({ ...seller, uen: event.target.value })} value={seller.uen} /><FieldError id="uen-error" message={sellerErrors.uen} /></label>
                <label className="field field--wide"><span>Invoice address <b>*</b></span><textarea aria-describedby="address-error" aria-invalid={Boolean(sellerErrors.address)} maxLength={500} onChange={(event) => setSeller({ ...seller, address: event.target.value })} rows={3} value={seller.address} /><FieldError id="address-error" message={sellerErrors.address} /></label>
                <label className="field"><span>Contact email <b>*</b></span><input aria-describedby="email-error" aria-invalid={Boolean(sellerErrors.email)} maxLength={200} onChange={(event) => setSeller({ ...seller, email: event.target.value })} type="email" value={seller.email} /><FieldError id="email-error" message={sellerErrors.email} /></label>
                <label className="field"><span>GST status</span><input disabled value="Not registered" /><span className="field-meta">GST cannot be enabled until a tax policy is implemented.</span></label>
                <label className="field field--wide"><span>Required buyer fields <b>*</b></span><textarea aria-describedby="buyer-fields-error buyer-fields-help" aria-invalid={Boolean(sellerErrors.requiredBuyerFields)} onChange={(event) => setBuyerFields(event.target.value)} rows={3} value={buyerFields} /><span className="field-meta" id="buyer-fields-help">One field per line, such as companyName or email.</span><FieldError id="buyer-fields-error" message={sellerErrors.requiredBuyerFields} /></label>
                <label className="field field--wide"><span>Invoice footer</span><textarea aria-describedby="footer-error footer-count" aria-invalid={Boolean(sellerErrors.invoiceFooter)} maxLength={1000} onChange={(event) => setSeller({ ...seller, invoiceFooter: event.target.value })} rows={3} value={seller.invoiceFooter} /><span className="field-meta" id="footer-count">{seller.invoiceFooter.length}/1,000 characters</span><FieldError id="footer-error" message={sellerErrors.invoiceFooter} /></label>
            </div>
            <div className="settings-actions"><Button disabled={sellerSave.isPending} type="submit">{sellerSave.isPending ? 'Saving…' : 'Save seller identity'}</Button></div>
        </form>

        <section className="panel form-section settings-section">
            <div className="section-copy settings-section__heading"><span className="launch-icon"><FileText aria-hidden="true" size={20} /></span><div><h2>Website payment instruction</h2><p>Publishing appends a version and immediately makes it active. Previous wording remains available for rollback.</p></div></div>
            {instructionError && <div className="dialog-error" role="alert">{instructionError}</div>}
            {instructionNotice && <div className="success-panel" role="status"><span>{instructionNotice}</span><Button onClick={() => setInstructionNotice(null)} size="compact" type="button" variant="ghost">Dismiss</Button></div>}
            <form className="instruction-publisher" onSubmit={submitInstruction}>
                <label className="field"><span>New instruction</span><textarea maxLength={4000} onChange={(event) => setInstructionText(event.target.value)} placeholder="Explain how the customer should complete external payment…" rows={5} value={instructionText} /><span className="field-meta">{instructionText.length}/4,000 characters</span></label>
                <Button disabled={publish.isPending} type="submit">{publish.isPending ? 'Publishing…' : 'Publish new version'}</Button>
            </form>
            <div className="instruction-history"><h3>Version history</h3>{instructions.map((item) => <article className={item.isActive ? 'instruction-version instruction-version--active' : 'instruction-version'} key={item.id}><div><strong>Version {item.version}</strong>{item.isActive && <StatusBadge status="Active" />}</div><p>{item.text}</p><footer><span><Clock3 aria-hidden="true" size={14} />{new Intl.DateTimeFormat(undefined, { dateStyle: 'medium', timeStyle: 'short' }).format(new Date(item.createdAt))}</span>{!item.isActive && <Button disabled={activate.isPending} onClick={() => activate.mutate(item.version)} size="compact" type="button" variant="secondary"><Check aria-hidden="true" size={14} />Activate</Button>}</footer></article>)}</div>
        </section>

        <form className="panel form-section settings-section" onSubmit={submitLimits} noValidate>
            <div className="section-copy settings-section__heading"><span className="launch-icon"><Clock3 aria-hidden="true" size={20} /></span><div><h2>Commerce limits</h2><p>These bounds protect partner composition and bulk requests. Amounts are USD with exactly two decimals.</p></div></div>
            {limitErrors.form && <div className="dialog-error" role="alert">{limitErrors.form}</div>}
            {limitsNotice && <div className="success-panel" role="status"><span>{limitsNotice}</span><Button onClick={() => setLimitsNotice(null)} size="compact" type="button" variant="ghost">Dismiss</Button></div>}
            <div className="form-grid">
                <label className="field"><span>Minimum order (USD) <b>*</b></span><input aria-describedby="min-usd-error" aria-invalid={Boolean(limitErrors.minUsd)} inputMode="decimal" onChange={(event) => setLimits({ ...limits, minUsd: event.target.value })} value={limits.minUsd} /><FieldError id="min-usd-error" message={limitErrors.minUsd} /></label>
                <label className="field"><span>Maximum order (USD) <b>*</b></span><input aria-describedby="max-usd-error" aria-invalid={Boolean(limitErrors.maxUsd)} inputMode="decimal" onChange={(event) => setLimits({ ...limits, maxUsd: event.target.value })} value={limits.maxUsd} /><FieldError id="max-usd-error" message={limitErrors.maxUsd} /></label>
                <label className="field"><span>Default bulk maximum <b>*</b></span><input aria-describedby="bulk-error" aria-invalid={Boolean(limitErrors.bulkDefaultMax)} max={1000} min={1} onChange={(event) => setLimits({ ...limits, bulkDefaultMax: Number(event.target.value) })} type="number" value={limits.bulkDefaultMax} /><FieldError id="bulk-error" message={limitErrors.bulkDefaultMax} /></label>
                <label className="field"><span>Composition line limit <b>*</b></span><input aria-describedby="lines-error" aria-invalid={Boolean(limitErrors.compositionMaxLines)} max={1000} min={1} onChange={(event) => setLimits({ ...limits, compositionMaxLines: Number(event.target.value) })} type="number" value={limits.compositionMaxLines} /><FieldError id="lines-error" message={limitErrors.compositionMaxLines} /></label>
                <label className="field field--wide"><span>Composition deadline (ms) <b>*</b></span><input aria-describedby="deadline-error deadline-help" aria-invalid={Boolean(limitErrors.compositionDeadlineMs)} max={9500} min={1000} onChange={(event) => setLimits({ ...limits, compositionDeadlineMs: Number(event.target.value) })} type="number" value={limits.compositionDeadlineMs} /><span className="field-meta" id="deadline-help">Must remain inside the platform’s 10-second response budget.</span><FieldError id="deadline-error" message={limitErrors.compositionDeadlineMs} /></label>
            </div>
            <div className="settings-actions"><Button disabled={limitsSave.isPending} type="submit">{limitsSave.isPending ? 'Saving…' : 'Save commerce limits'}</Button></div>
        </form>
    </section>;
}
