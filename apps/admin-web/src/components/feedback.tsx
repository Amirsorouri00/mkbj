import { Button } from '@mkbj/ui';
import { AlertTriangle, Box, LoaderCircle } from 'lucide-react';
import type { ReactNode } from 'react';
import { Link } from 'react-router-dom';
import { toAppError } from '../api/errors';

export function PageLoading({ label = 'Loading' }: { label?: string }) {
  return (
    <div className="page-state" role="status">
      <LoaderCircle aria-hidden="true" className="spin" size={24} />
      <span>{label}…</span>
    </div>
  );
}

export function EmptyState({ title, children }: { title: string; children: ReactNode }) {
  return (
    <div className="empty-state">
      <Box aria-hidden="true" size={28} />
      <h2>{title}</h2>
      <p>{children}</p>
    </div>
  );
}

export function ErrorState({ error, retry }: { error: unknown; retry?: () => void }) {
  const normalized = toAppError(error);
  if (normalized.status === 403) {
    return (
      <div className="empty-state" role="alert">
        <AlertTriangle aria-hidden="true" size={28} />
        <h2>Access restricted</h2>
        <p>{normalized.message}</p>
        <Button asChild variant="secondary"><Link to="/">Return home</Link></Button>
      </div>
    );
  }
  return (
    <div className="error-panel" role="alert">
      <AlertTriangle aria-hidden="true" size={20} />
      <div>
        <strong>We couldn’t load this page.</strong>
        <p>{normalized.message}</p>
        {normalized.correlationId && <small>Reference: {normalized.correlationId}</small>}
      </div>
      {retry && <Button onClick={retry} variant="secondary">Try again</Button>}
    </div>
  );
}
