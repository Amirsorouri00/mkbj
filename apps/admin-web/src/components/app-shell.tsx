import * as Dialog from '@radix-ui/react-dialog';
import * as DropdownMenu from '@radix-ui/react-dropdown-menu';
import { Button, cn } from '@mkbj/ui';
import {
    Box,
    Activity,
    ChevronDown,
    ChevronRight,
    CircleDollarSign,
    FileInput,
    FolderTree,
    ListFilter,
    LogOut,
    Menu,
    ReceiptText,
    Settings,
    Tag,
    Users,
    X,
} from 'lucide-react';
import { useEffect, useRef, useState } from 'react';
import { Link, NavLink, Outlet, useLocation } from 'react-router-dom';
import { useAuth } from '../auth/auth-context';

const navigation = [
    { to: '/catalog/products', label: 'Products', icon: Box },
    { to: '/catalog/categories', label: 'Categories', icon: FolderTree },
    { to: '/catalog/brands', label: 'Brands', icon: Tag },
    { to: '/catalog/attributes', label: 'Attributes', icon: ListFilter },
    { to: '/imports', label: 'Imports', icon: FileInput },
    { to: '/customers', label: 'Customers', icon: Users },
    { to: '/orders', label: 'Orders & invoices', icon: ReceiptText },
    { to: '/partner-requests', label: 'API requests', icon: Activity },
    { to: '/pricing', label: 'Pricing', icon: CircleDollarSign },
    { to: '/settings', label: 'Settings', icon: Settings },
];

function Navigation({ close, showSettings }: { close?: () => void; showSettings: boolean }) {
    return (
        <nav aria-label="Primary navigation" className="sidebar-nav">
            <p className="sidebar-section-label">Workspace</p>
            {navigation.filter((item) => item.to !== '/settings' || showSettings).map(({ to, label, icon: Icon }) => (
                <NavLink
                    className={({ isActive }) => cn('nav-link', isActive && 'nav-link--active')}
                    key={to}
                    {...(close ? { onClick: close } : {})}
                    to={to}
                >
                    <Icon aria-hidden="true" size={18} />
                    {label}
                </NavLink>
            ))}
        </nav>
    );
}

export interface BreadcrumbItem {
    label: string;
    to?: string;
}

export function buildBreadcrumbs(pathname: string): ReadonlyArray<BreadcrumbItem> {
    const segments = pathname.split('/').filter(Boolean);
    if (segments[0] !== 'catalog') {
        const topLevel: Record<string, string> = {
            imports: 'Imports',
            customers: 'Customers',
            orders: 'Orders & invoices',
            'partner-requests': 'API requests',
            pricing: 'Pricing',
            settings: 'Settings',
        };
        const label = topLevel[segments[0] ?? ''];
        if (!label) return [{ label: 'Home', to: '/' }, { label: 'Page not found' }];
        return segments.length > 1
            ? [
                  { label: 'Home', to: '/' },
                  { label, to: `/${segments[0]}` },
                  {
                      label:
                          segments[0] === 'customers'
                              ? 'Customer detail'
                              : segments[0] === 'orders'
                                ? 'Order detail'
                                : 'Detail',
                  },
              ]
            : [{ label: 'Home', to: '/' }, { label }];
    }

    const section = segments[1];
    const sectionLabel =
        section === 'products'
            ? 'Products'
            : section === 'categories'
              ? 'Categories'
              : section === 'brands'
                ? 'Brands'
                : section === 'attributes'
                  ? 'Attributes'
                  : 'Catalog';
    const sectionPath = section && section !== 'catalog' ? `/catalog/${section}` : undefined;
    const items: BreadcrumbItem[] = [{ label: 'Home', to: '/' }, { label: 'Catalog' }];
    if (segments.length === 2) return [...items, { label: sectionLabel }];

    items.push({ label: sectionLabel, ...(sectionPath ? { to: sectionPath } : {}) });
    if (section === 'products') {
        if (segments[2] === 'new') items.push({ label: 'New product' });
        else if (segments[3] === 'edit') items.push({ label: 'Edit product' });
        else items.push({ label: 'Product detail' });
    } else if (section === 'categories' && segments[3] === 'attributes') {
        items.push({ label: 'Category attributes' });
    } else {
        items.push({ label: 'Detail' });
    }
    return items;
}

function Breadcrumbs({ pathname }: { pathname: string }) {
    const items = buildBreadcrumbs(pathname);
    return (
        <nav aria-label="Breadcrumb" className="breadcrumbs">
            {items.map((item, index) => {
                const current = index === items.length - 1;
                return (
                    <span key={`${item.label}-${index}`}>
                        {index > 0 && <ChevronRight aria-hidden="true" size={14} />}
                        {current ? (
                            <span aria-current="page">{item.label}</span>
                        ) : item.to ? (
                            <Link to={item.to}>{item.label}</Link>
                        ) : (
                            <span>{item.label}</span>
                        )}
                    </span>
                );
            })}
        </nav>
    );
}

export function AppShell() {
    const [mobileOpen, setMobileOpen] = useState(false);
    const { profile, roles, logout } = useAuth();
    const location = useLocation();
    const mainRef = useRef<HTMLElement>(null);
    const displayName =
        [profile?.firstName, profile?.lastName].filter(Boolean).join(' ') ||
        profile?.username ||
        'MKBJ staff';

    useEffect(() => mainRef.current?.focus(), [location.pathname]);

    return (
        <div className="app-frame">
            <a className="skip-link" href="#main-content">
                Skip to main content
            </a>
            <aside className="desktop-sidebar">
                <Link aria-label="MKBJ admin home" className="wordmark" to="/">
                    MKBJ
                </Link>
                <Navigation showSettings={roles.includes('super_admin')} />
                <div className="sidebar-foot">Backoffice</div>
            </aside>

            <div className="app-column">
                <header className="topbar">
                    <Dialog.Root onOpenChange={setMobileOpen} open={mobileOpen}>
                        <Dialog.Trigger asChild>
                            <Button
                                aria-label="Open navigation"
                                className="mobile-menu"
                                size="icon"
                                variant="ghost"
                            >
                                <Menu />
                            </Button>
                        </Dialog.Trigger>
                        <Dialog.Portal>
                            <Dialog.Overlay className="drawer-overlay" />
                            <Dialog.Content aria-describedby={undefined} className="drawer-panel">
                                <div className="drawer-head">
                                    <Dialog.Title className="wordmark">MKBJ</Dialog.Title>
                                    <Dialog.Close asChild>
                                        <Button
                                            aria-label="Close navigation"
                                            size="icon"
                                            variant="ghost"
                                        >
                                            <X />
                                        </Button>
                                    </Dialog.Close>
                                </div>
                                <Navigation close={() => setMobileOpen(false)} showSettings={roles.includes('super_admin')} />
                            </Dialog.Content>
                        </Dialog.Portal>
                    </Dialog.Root>
                    <div className="mobile-wordmark">MKBJ</div>
                    <DropdownMenu.Root>
                        <DropdownMenu.Trigger asChild>
                            <Button
                                aria-label="Open account menu"
                                className="account-summary"
                                variant="ghost"
                            >
                                <div>
                                    <strong>{displayName}</strong>
                                    <span>Staff account</span>
                                </div>
                                <ChevronDown aria-hidden="true" size={16} />
                            </Button>
                        </DropdownMenu.Trigger>
                        <DropdownMenu.Portal>
                            <DropdownMenu.Content
                                align="end"
                                className="action-menu account-menu"
                                sideOffset={8}
                            >
                                <DropdownMenu.Label className="account-menu__label">
                                    <strong>{displayName}</strong>
                                    <span>Backoffice staff</span>
                                </DropdownMenu.Label>
                                <DropdownMenu.Separator />
                                <DropdownMenu.Item onSelect={() => void logout()}>
                                    <LogOut aria-hidden="true" size={16} />
                                    Sign out
                                </DropdownMenu.Item>
                            </DropdownMenu.Content>
                        </DropdownMenu.Portal>
                    </DropdownMenu.Root>
                </header>
                <main id="main-content" ref={mainRef} tabIndex={-1}>
                    <Breadcrumbs pathname={location.pathname} />
                    <Outlet />
                </main>
            </div>
        </div>
    );
}
