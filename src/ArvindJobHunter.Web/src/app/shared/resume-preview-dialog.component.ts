import { Component, inject } from '@angular/core';
import { DynamicDialogConfig, DynamicDialogRef } from 'primeng/dynamicdialog';
import { ButtonModule } from 'primeng/button';
import { ResumePreview } from '../core/models';

@Component({
  selector: 'app-resume-preview-dialog',
  standalone: true,
  imports: [ButtonModule],
  templateUrl: './resume-preview-dialog.component.html',
  styleUrl: './resume-preview-dialog.component.scss'
})
export class ResumePreviewDialogComponent {
  readonly data = inject<DynamicDialogConfig<ResumePreview>>(DynamicDialogConfig).data!;
  readonly ref = inject(DynamicDialogRef);
}
