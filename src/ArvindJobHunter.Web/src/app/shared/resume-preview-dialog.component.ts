import { Component, inject } from '@angular/core';
import { DynamicDialogConfig, DynamicDialogRef } from 'primeng/dynamicdialog';
import { ButtonModule } from 'primeng/button';
import { ResumePreview } from '../core/models';

@Component({
  selector: 'app-resume-preview-dialog',
  standalone: true,
  imports: [ButtonModule],
  template: `
    <h2 class="dialog-title row">
      <span class="material-symbols-rounded">preview</span>
      Resume preview
    </h2>
    <div class="dialog-content">
      <div class="summary">
        <span class="chip">{{ data.status }}</span>
        <span class="muted small">Paragraphs: {{ data.paragraphs.length }}</span>
      </div>
      @if (data.templateFidelity; as f) {
        @if (f.templatePreserved) {
  <div class="alert info mt"><span class="material-symbols-rounded">check_circle</span><span>Template preserved ({{ f.tailoredPageEstimate }} page(s)).</span></div>
} @else {
  <div class="alert warn mt"><span class="material-symbols-rounded">warning</span>
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
    </div>
    <div class="dialog-actions">
      <p-button label="Close" (onClick)="ref.close()" />
    </div>
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
  readonly data = inject<DynamicDialogConfig<ResumePreview>>(DynamicDialogConfig).data!;
  readonly ref = inject(DynamicDialogRef);
}
