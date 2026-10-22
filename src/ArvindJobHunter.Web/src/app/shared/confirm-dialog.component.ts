import { Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { DynamicDialogConfig, DynamicDialogRef } from 'primeng/dynamicdialog';
import { ButtonModule } from 'primeng/button';
import { InputTextModule } from 'primeng/inputtext';
import { FloatLabelModule } from 'primeng/floatlabel';
import { AutoFocusModule } from 'primeng/autofocus';

export interface ConfirmDialogData {
  title: string;
  message: string;
  confirmLabel?: string;
  cancelLabel?: string;
  danger?: boolean;
  /** When set, shows a text input and resolves with the entered string (empty string if left blank). */
  promptLabel?: string;
}

@Component({
  selector: 'app-confirm-dialog',
  standalone: true,
  imports: [FormsModule, ButtonModule, InputTextModule, FloatLabelModule, AutoFocusModule],
  template: `
    <h2 class="dialog-title row">
      <span class="material-symbols-rounded" [class.danger]="data.danger">{{ data.danger ? 'warning' : 'help' }}</span>
      {{ data.title }}
    </h2>
    <div class="dialog-content">
      <p class="pre">{{ data.message }}</p>
      @if (data.promptLabel) {
        <p-floatlabel variant="on" class="mt full">
          <input pInputText id="confirm-prompt" class="full" [(ngModel)]="value" [pAutoFocus]="true" />
          <label for="confirm-prompt">{{ data.promptLabel }}</label>
        </p-floatlabel>
      }
    </div>
    <div class="dialog-actions">
      <p-button [text]="true" [label]="data.cancelLabel ?? 'Cancel'" (onClick)="ref.close(undefined)" />
      <p-button [severity]="data.danger ? 'danger' : 'primary'" [label]="data.confirmLabel ?? 'Confirm'" (onClick)="ref.close(data.promptLabel ? value : true)" />
    </div>
  `,
  styles: [`.danger { color: var(--ajh-danger); } h2 { gap: .5rem; }`]
})
export class ConfirmDialogComponent {
  readonly data = inject<DynamicDialogConfig<ConfirmDialogData>>(DynamicDialogConfig).data!;
  readonly ref = inject(DynamicDialogRef);
  value = '';
}
