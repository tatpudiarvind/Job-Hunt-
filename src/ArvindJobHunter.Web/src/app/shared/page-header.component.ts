import { Component, input } from '@angular/core';
import { MatIconModule } from '@angular/material/icon';

@Component({
  selector: 'app-page-header',
  standalone: true,
  imports: [MatIconModule],
  template: `
    <header class="header">
      <div class="text">
        @if (eyebrow()) { <p class="eyebrow">{{ eyebrow() }}</p> }
        <h1 class="row"><mat-icon class="material-symbols-rounded">{{ icon() }}</mat-icon>{{ title() }}</h1>
        @if (subtitle()) { <p class="muted">{{ subtitle() }}</p> }
      </div>
      <div class="actions"><ng-content /></div>
    </header>
  `,
  styles: [`
    .header { display: flex; flex-wrap: wrap; justify-content: space-between; align-items: flex-end; gap: 1rem; }
    .text { display: grid; gap: .35rem; }
    .eyebrow { font-size: .72rem; font-weight: 600; letter-spacing: .09em; text-transform: uppercase; color: var(--ajh-primary); }
    h1 { font-size: 1.6rem; gap: .5rem; }
    h1 mat-icon { color: var(--ajh-primary); font-size: 1.8rem; width: 1.8rem; height: 1.8rem; }
    .actions { display: flex; gap: .75rem; flex-wrap: wrap; }
  `]
})
export class PageHeaderComponent {
  readonly title = input.required<string>();
  readonly icon = input('dashboard');
  readonly eyebrow = input('');
  readonly subtitle = input('');
}
