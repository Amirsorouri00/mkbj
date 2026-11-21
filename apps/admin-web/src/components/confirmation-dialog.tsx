import * as Dialog from '@radix-ui/react-dialog';
import { Button } from '@mkbj/ui';
import { X } from 'lucide-react';

interface ConfirmationDialogProps {
  open: boolean;
  onOpenChange: (open: boolean) => void;
  title: string;
  description: string;
  confirmLabel: string;
  pending?: boolean;
  confirmDisabled?: boolean;
  destructive?: boolean;
  onConfirm: () => void;
}

export function ConfirmationDialog({
  open,
  onOpenChange,
  title,
  description,
  confirmLabel,
  pending = false,
  confirmDisabled = false,
  destructive = false,
  onConfirm,
}: ConfirmationDialogProps) {
  return (
    <Dialog.Root onOpenChange={onOpenChange} open={open}>
      <Dialog.Portal>
        <Dialog.Overlay className="dialog-overlay" />
        <Dialog.Content aria-describedby="confirmation-description" className="dialog-panel">
          <div className="dialog-heading">
            <Dialog.Title>{title}</Dialog.Title>
            <Dialog.Close asChild><Button aria-label="Close dialog" disabled={pending} size="icon" variant="ghost"><X size={18} /></Button></Dialog.Close>
          </div>
          <Dialog.Description id="confirmation-description">{description}</Dialog.Description>
          <div className="dialog-actions">
            <Dialog.Close asChild><Button disabled={pending} variant="secondary">Cancel</Button></Dialog.Close>
            <Button disabled={pending || confirmDisabled} onClick={onConfirm} variant={destructive ? 'destructive' : 'primary'}>{pending ? 'Working…' : confirmLabel}</Button>
          </div>
        </Dialog.Content>
      </Dialog.Portal>
    </Dialog.Root>
  );
}
