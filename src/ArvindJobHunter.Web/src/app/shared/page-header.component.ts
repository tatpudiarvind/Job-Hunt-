import { Component, input } from '@angular/core';
@Component({
  selector: 'app-page-header',
  standalone: true,
  templateUrl: './page-header.component.html',
  styleUrl: './page-header.component.scss'
})
export class PageHeaderComponent {
  readonly title = input.required<string>();
  readonly icon = input('dashboard');
  readonly eyebrow = input('');
  readonly subtitle = input('');
}
