// @vitest-environment jsdom
import { render, screen } from '@testing-library/react';
import userEvent from '@testing-library/user-event';
import { describe, expect, it, vi } from 'vitest';
import { ConfirmationDialog } from './confirmation-dialog';

describe('ConfirmationDialog', () => {
  it('requires an enabled explicit confirmation', async () => {
    const confirm = vi.fn();
    const user = userEvent.setup();
    const { rerender } = render(<ConfirmationDialog confirmDisabled confirmLabel="Delete" description="Permanent action" onConfirm={confirm} onOpenChange={() => undefined} open title="Delete record?" />);
    expect(screen.getByRole<HTMLButtonElement>('button', { name: 'Delete' }).disabled).toBe(true);
    rerender(<ConfirmationDialog confirmLabel="Delete" description="Permanent action" onConfirm={confirm} onOpenChange={() => undefined} open title="Delete record?" />);
    await user.click(screen.getByRole('button', { name: 'Delete' }));
    expect(confirm).toHaveBeenCalledOnce();
  });
});
