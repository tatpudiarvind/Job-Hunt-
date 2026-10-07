import { Component, inject } from '@angular/core';
import { FormsModule } from '@angular/forms';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatFormFieldModule } from '@angular/material/form-field';
import { MatInputModule } from '@angular/material/input';
import { MatIconModule } from '@angular/material/icon';

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
  imports: [FormsModule, MatDialogModule, MatButtonModule, MatFormFieldModule, MatInputModule, MatIconModule],
  template: `
    <h2 mat-dialog-title class="row">
      <mat-icon class="material-symbols-rounded" [class.danger]="data.danger">{{ data.danger ? 'warning' : 'help' }}</mat-icon>
      {{ data.title }}
    </h2>
    <mat-dialog-content>
      <p class="pre">{{ data.message }}</p>
      @if (data.promptLabel) {
        <mat-form-field appearance="outline" class="mt">
          <mat-label>{{ data.promptLabel }}</mat-label>
          <input matInput [(ngModel)]="value" cdkFocusInitial />
        </mat-form-field>
      }
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-button (click)="ref.close(undefined)">{{ data.cancelLabel ?? 'Cancel' }}</button>
      <button mat-flat-button [color]="data.danger ? 'warn' : 'primary'" (click)="ref.close(data.promptLabel ? value : true)">{{ data.confirmLabel ?? 'Confirm' }}</button>
    </mat-dialog-actions>
  `,
  styles: [`mat-icon.danger { color: var(--ajh-danger); } h2 { gap: .5rem; }`]
})
export class ConfirmDialogComponent {
  readonly data = inject<ConfirmDialogData>(MAT_DIALOG_DATA);
  readonly ref = inject(MatDialogRef<ConfirmDialogComponent>);
  value = '';
}
