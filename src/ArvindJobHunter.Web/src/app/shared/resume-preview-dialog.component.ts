import { Component, inject } from '@angular/core';
import { MAT_DIALOG_DATA, MatDialogModule, MatDialogRef } from '@angular/material/dialog';
import { MatButtonModule } from '@angular/material/button';
import { MatIconModule } from '@angular/material/icon';
import { ResumePreview } from '../core/models';

@Component({
  selector: 'app-resume-preview-dialog',
  standalone: true,
  imports: [MatDialogModule, MatButtonModule, MatIconModule],
  template: `
    <h2 mat-dialog-title class="row">
      <mat-icon class="material-symbols-rounded">preview</mat-icon>
      Resume preview
    </h2>
    <mat-dialog-content>
      <div class="summary">
        <span class="chip">{{ data.status }}</span>
        <span class="muted small">Paragraphs: {{ data.paragraphs.length }}</span>
      </div>
      @if (data.templateFidelity; as f) {
        @if (f.templatePreserved) {
          <div class="alert info mt"><mat-icon class="material-symbols-rounded">check_circle</mat-icon><span>Template preserved ({{ f.tailoredPageEstimate }} page(s)).</span></div>
        } @else {
          <div class="alert warn mt"><mat-icon class="material-symbols-rounded">warning</mat-icon>
            <div><b>Template check</b>
              <ul class="issues">@for (issue of f.issues; track issue) { <li>{{ issue }}</li> }</ul>
            </div>
          </div>
        }
      }
      <div class="preview mt">
        @for (p of data.paragraphs; track p.index) {
          <p class="para" [class.style-heading]="p.style && p.style !== 'Normal'">
            {{ p.text }}
          </p>
        }
        @if (!data.paragraphs.length) {
          <div class="empty">No preview content was returned.</div>
        }
      </div>
    </mat-dialog-content>
    <mat-dialog-actions align="end">
      <button mat-flat-button color="primary" (click)="ref.close()">Close</button>
    </mat-dialog-actions>
  `,
  styles: [`
    .summary { display: flex; align-items: center; gap: .75rem; flex-wrap: wrap; }
    .chip { display: inline-flex; align-items: center; padding: .25rem .65rem; border-radius: 999px; background: var(--ajh-surface-variant); font-size: .75rem; font-weight: 600; text-transform: uppercase; letter-spacing: .04em; }
    .preview { max-height: min(70vh, 720px); overflow: auto; border: 1px solid var(--ajh-border); border-radius: 12px; padding: 1rem; background: var(--ajh-surface); }
    .para { margin: 0 0 .75rem; white-space: pre-wrap; line-height: 1.6; }
    .para.style-heading { font-weight: 600; }
    .issues { margin: .25rem 0 0; padding-left: 1.1rem; }
  `]
})
export class ResumePreviewDialogComponent {
  readonly data = inject<ResumePreview>(MAT_DIALOG_DATA);
  readonly ref = inject(MatDialogRef<ResumePreviewDialogComponent>);
}
