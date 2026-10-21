import { Injectable, Type, inject } from '@angular/core';
import { MessageService } from 'primeng/api';
import { DialogService } from 'primeng/dynamicdialog';
import { firstValueFrom } from 'rxjs';
import { ConfirmDialogComponent, ConfirmDialogData } from './confirm-dialog.component';
import { ResumePreview } from '../core/models';
import { ResumePreviewDialogComponent } from './resume-preview-dialog.component';

/** Centralized toasts and confirmation dialogs so pages never call window.confirm/prompt. */
@Injectable({ providedIn: 'root' })
export class NotifyService {
  private readonly messages = inject(MessageService);
  private readonly dialog = inject(DialogService);

  success(message: string): void { this.messages.add({ severity: 'success', detail: message, life: 4000 }); }
  info(message: string): void { this.messages.add({ severity: 'info', detail: message, life: 5000 }); }
  error(message: string): void { this.messages.add({ severity: 'error', detail: message, life: 8000 }); }

  async confirm(data: ConfirmDialogData): Promise<boolean> {
    return (await this.open(ConfirmDialogComponent, data, '440px')) === true;
  }

  /** Resolves null when cancelled, otherwise the entered text (may be empty). */
  async prompt(data: ConfirmDialogData & { promptLabel: string }): Promise<string | null> {
    const result = await this.open(ConfirmDialogComponent, data, '440px');
    return typeof result === 'string' ? result : null;
  }

  previewResume(data: ResumePreview): void {
    void this.open(ResumePreviewDialogComponent, data, '960px');
  }

  private async open<T>(component: Type<T>, data: unknown, width: string): Promise<unknown> {
    const ref = this.dialog.open(component, {
      data, width, modal: true, showHeader: false, closeOnEscape: true, dismissableMask: false, focusOnShow: false,
      style: { maxWidth: '96vw' }
    });
    return ref ? firstValueFrom(ref.onClose) : undefined;
  }
}
