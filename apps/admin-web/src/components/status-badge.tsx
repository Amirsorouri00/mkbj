import { cn } from '@mkbj/ui';

export function StatusBadge({ status }: { status: string }) {
  const normalized = status.toLowerCase();
  const positive = normalized === 'active' || normalized === 'published';
  const warning = normalized === 'draft' || normalized === 'pending';
  return (
    <span
      className={cn('status-badge', positive && 'status-badge--positive', warning && 'status-badge--warning')}
    >
      <span aria-hidden="true" className="status-badge__dot" />
      {status}
    </span>
  );
}
