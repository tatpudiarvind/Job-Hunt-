import { Injectable, inject } from '@angular/core';
import { MatSnackBar } from '@angular/material/snack-bar';
import { MatDialog } from '@angular/material/dialog';
import { firstValueFrom } from 'rxjs';
import { ConfirmDialogComponent, ConfirmDialogData } from './confirm-dialog.component';

/** Centralized toasts and confirmation dialogs so pages never call window.confirm/prompt. */
@Injectable({ providedIn: 'root' })
export class NotifyService {
  private readonly snack = inject(MatSnackBar);
  private readonly dialog = inject(MatDialog);

  success(message: string): void { this.snack.open(message, 'OK', { duration: 4000, panelClass: 'success' }); }
  info(message: string): void { this.snack.open(message, 'OK', { duration: 5000 }); }
  error(message: string): void { this.snack.open(message, 'Dismiss', { duration: 8000, panelClass: 'error' }); }

  async confirm(data: ConfirmDialogData): Promise<boolean> {
    const result = await firstValueFrom(this.dialog.open(ConfirmDialogComponent, { data, width: '440px', autoFocus: false }).afterClosed());
    return result === true;
  }

  /** Resolves null when cancelled, otherwise the entered text (may be empty). */
  async prompt(data: ConfirmDialogData & { promptLabel: string }): Promise<string | null> {
    const result = await firstValueFrom(this.dialog.open(ConfirmDialogComponent, { data, width: '440px', autoFocus: false }).afterClosed());
    return typeof result === 'string' ? result : null;
  }
}
