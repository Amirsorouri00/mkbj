import { Button } from '@mkbj/ui';
import { QueryClient, QueryClientProvider } from '@tanstack/react-query';
import { LockKeyhole, LoaderCircle } from 'lucide-react';
import { lazy, Suspense, useState } from 'react';
import { BrowserRouter, Navigate, Route, Routes, useLocation } from 'react-router-dom';
import { AuthProvider, useAuth } from './auth/auth-context';
import { AppShell } from './components/app-shell';
import { AppErrorBoundary } from './components/app-error-boundary';
import { PageLoading } from './components/feedback';

const ProductListPage = lazy(() =>
    import('./features/products/product-list-page').then((module) => ({
        default: module.ProductListPage,
    })),
);
const ProductDetailPage = lazy(() =>
    import('./features/products/product-detail-page').then((module) => ({
        default: module.ProductDetailPage,
    })),
);
const ProductEditorPage = lazy(() =>
    import('./features/products/product-editor-page').then((module) => ({
        default: module.ProductEditorPage,
    })),
);
const BrandListPage = lazy(() =>
    import('./features/brands/brand-list-page').then((module) => ({
        default: module.BrandListPage,
    })),
);
const CategoryListPage = lazy(() =>
    import('./features/categories/category-list-page').then((module) => ({
        default: module.CategoryListPage,
    })),
);
const AttributeListPage = lazy(() =>
    import('./features/attributes/attribute-list-page').then((module) => ({
        default: module.AttributeListPage,
    })),
);
const CategoryAttributePage = lazy(() =>
    import('./features/categories/category-attribute-page').then((module) => ({
        default: module.CategoryAttributePage,
    })),
);
const CustomerListPage = lazy(() =>
    import('./features/customers/customer-list-page').then((module) => ({
        default: module.CustomerListPage,
    })),
);
const CustomerDetailPage = lazy(() =>
    import('./features/customers/customer-detail-page').then((module) => ({
        default: module.CustomerDetailPage,
    })),
);
const OrderListPage = lazy(() =>
    import('./features/orders/order-list-page').then((module) => ({
        default: module.OrderListPage,
    })),
);
const OrderDetailPage = lazy(() =>
    import('./features/orders/order-detail-page').then((module) => ({
        default: module.OrderDetailPage,
    })),
);
const PricingPage = lazy(() =>
    import('./features/pricing/pricing-page').then((module) => ({ default: module.PricingPage })),
);
const SettingsPage = lazy(() =>
    import('./features/settings/settings-page').then((module) => ({ default: module.SettingsPage })),
);
const PartnerRequestsPage = lazy(() =>
    import('./features/orders/partner-requests-page').then((module) => ({
        default: module.PartnerRequestsPage,
    })),
);
const PartnerRequestDetailPage = lazy(() =>
    import('./features/orders/partner-requests-page').then((module) => ({
        default: module.PartnerRequestDetailPage,
    })),
);
const ImportsPage = lazy(() =>
    import('./features/imports/imports-page').then((module) => ({ default: module.ImportsPage })),
);
const ImportJobDetailPage = lazy(() =>
    import('./features/imports/import-job-detail-page').then((module) => ({
        default: module.ImportJobDetailPage,
    })),
);
const StagedRecordDetailPage = lazy(() =>
    import('./features/imports/staged-record-detail-page').then((module) => ({
        default: module.StagedRecordDetailPage,
    })),
);
const NotFoundPage = lazy(() =>
    import('./pages').then((module) => ({ default: module.NotFoundPage })),
);

function AuthGate() {
    const auth = useAuth();
    const location = useLocation();
    if (!auth.ready) {
        return (
            <div className="auth-state" role="status">
                <LoaderCircle className="spin" />
                <strong>Restoring your secure session…</strong>
            </div>
        );
    }
    if (!auth.authenticated) {
        return (
            <main className="login-page">
                <section className="login-card" aria-labelledby="login-title">
                    <div className="login-wordmark">MKBJ</div>
                    <span className="login-icon">
                        <LockKeyhole aria-hidden="true" />
                    </span>
                    <p className="eyebrow">Backoffice</p>
                    <h1 id="login-title">Sign in to continue</h1>
                    <p>Use your MKBJ staff account to access catalog and commerce operations.</p>
                    {auth.initializationError && (
                        <div className="login-error" role="alert">
                            {auth.initializationError}
                        </div>
                    )}
                    <Button
                        disabled={Boolean(auth.initializationError)}
                        onClick={() => void auth.login()}
                    >
                        Sign in securely
                    </Button>
                    <small>You will continue through the company identity service.</small>
                </section>
            </main>
        );
    }
    if (!auth.roles.includes('staff') && !auth.roles.includes('super_admin')) {
        return (
            <main className="login-page">
                <section className="login-card" aria-labelledby="forbidden-title">
                    <div className="login-wordmark">MKBJ</div>
                    <span className="login-icon">
                        <LockKeyhole aria-hidden="true" />
                    </span>
                    <p className="eyebrow">Backoffice</p>
                    <h1 id="forbidden-title">Access restricted</h1>
                    <p>Your account is signed in but does not have a Backoffice staff role.</p>
                    <Button onClick={() => void auth.logout()} variant="secondary">
                        Sign out
                    </Button>
                </section>
            </main>
        );
    }
    return (
        <AppErrorBoundary resetKey={location.pathname}>
            <Suspense fallback={<PageLoading label="Loading page" />}>
                <Routes>
                    <Route element={<AppShell />}>
                        <Route element={<Navigate replace to="/catalog/products" />} index />
                        <Route element={<ProductListPage />} path="catalog/products" />
                        <Route element={<ProductEditorPage />} path="catalog/products/new" />
                        <Route element={<ProductEditorPage />} path="catalog/products/:id/edit" />
                        <Route element={<ProductDetailPage />} path="catalog/products/:id" />
                        <Route element={<BrandListPage />} path="catalog/brands" />
                        <Route element={<CategoryListPage />} path="catalog/categories" />
                        <Route
                            element={<CategoryAttributePage />}
                            path="catalog/categories/:id/attributes"
                        />
                        <Route element={<AttributeListPage />} path="catalog/attributes" />
                        <Route element={<ImportsPage />} path="imports" />
                        <Route element={<ImportJobDetailPage />} path="imports/:id" />
                        <Route element={<StagedRecordDetailPage />} path="imports/records/:id" />
                        <Route element={<CustomerListPage />} path="customers" />
                        <Route element={<CustomerDetailPage />} path="customers/:id" />
                        <Route element={<OrderListPage />} path="orders" />
                        <Route element={<OrderDetailPage />} path="orders/:id" />
                        <Route element={<PricingPage />} path="pricing" />
                        <Route element={<SettingsPage />} path="settings" />
                        <Route element={<PartnerRequestsPage />} path="partner-requests" />
                        <Route element={<PartnerRequestDetailPage />} path="partner-requests/:id" />
                        <Route element={<NotFoundPage />} path="*" />
                    </Route>
                </Routes>
            </Suspense>
        </AppErrorBoundary>
    );
}

export function App() {
    const [queryClient] = useState(
        () =>
            new QueryClient({
                defaultOptions: {
                    queries: {
                        retry: (count, error) =>
                            count < 2 &&
                            !(
                                error instanceof Error &&
                                'status' in error &&
                                (error as { status?: number }).status === 403
                            ),
                        staleTime: 30_000,
                    },
                },
            }),
    );
    return (
        <QueryClientProvider client={queryClient}>
            <AuthProvider>
                <BrowserRouter>
                    <AuthGate />
                </BrowserRouter>
            </AuthProvider>
        </QueryClientProvider>
    );
}
