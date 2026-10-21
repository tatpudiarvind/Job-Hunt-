import { Component, computed, input } from '@angular/core';
import { SkeletonModule } from 'primeng/skeleton';

export type LoadingSkeletonVariant = 'list' | 'table' | 'cards' | 'detail' | 'dashboard' | 'form';

/**
 * Content-shaped loading placeholder built on PrimeNG Skeleton.
 * Use for initial page/data loads; keep mat-progress-bar for in-place actions.
 */
@Component({
  selector: 'app-loading-skeleton',
  standalone: true,
  imports: [SkeletonModule],
  host: { role: 'status', 'aria-busy': 'true', '[attr.aria-label]': 'label()', '[style.--cols]': 'gridColumns()' },
  template: `
    @switch (variant()) {
      @case ('dashboard') {
        <div class="grid-3 metrics">
          @for (i of rowList(); track i) {
            <div class="metric"><p-skeleton width="3rem" height="1.8rem" /><p-skeleton width="60%" height=".8rem" /></div>
          }
        </div>
        <div class="grid-2">
          @for (i of [1, 2, 3, 4]; track i) {
            <section class="card stack">
              <p-skeleton width="45%" height="1.1rem" />
              @for (j of [1, 2, 3]; track j) { <p-skeleton height=".9rem" [width]="j === 3 ? '70%' : '100%'" /> }
            </section>
          }
        </div>
      }
      @case ('table') {
        <section class="card stack">
          <div class="trow head">@for (c of colList(); track c) { <p-skeleton height=".75rem" width="60%" /> }</div>
          @for (r of rowList(); track r) {
            <div class="trow">@for (c of colList(); track c) { <p-skeleton height="1rem" [width]="c === 1 ? '90%' : '70%'" /> }</div>
          }
        </section>
      }
      @case ('cards') {
        <div class="grid-2">
          @for (i of rowList(); track i) {
            <section class="card stack">
              <div class="lead"><p-skeleton shape="circle" size="2.5rem" /><div class="grow stack tight"><p-skeleton width="55%" height="1rem" /><p-skeleton width="35%" height=".8rem" /></div></div>
              <p-skeleton height=".85rem" /><p-skeleton height=".85rem" width="80%" />
              <div class="lead"><p-skeleton width="6rem" height="2rem" borderRadius="999px" /><p-skeleton width="6rem" height="2rem" borderRadius="999px" /></div>
            </section>
          }
        </div>
      }
      @case ('detail') {
        <section class="card stack">
          <div class="lead"><p-skeleton width="40%" height="1.6rem" /><p-skeleton width="5rem" height="1.6rem" borderRadius="999px" /></div>
          <p-skeleton width="30%" height=".9rem" />
          @for (i of rowList(); track i) { <p-skeleton height=".85rem" [width]="i % 3 === 0 ? '65%' : '100%'" /> }
        </section>
      }
      @case ('form') {
        <section class="card stack">
          <p-skeleton width="35%" height="1.1rem" />
          @for (i of rowList(); track i) { <p-skeleton height="3.25rem" borderRadius="6px" /> }
          <div class="lead end"><p-skeleton width="7rem" height="2.4rem" borderRadius="999px" /></div>
        </section>
      }
      @default {
        <section class="card stack">
          @for (i of rowList(); track i) {
            <div class="lead item">
              <p-skeleton shape="circle" size="2.25rem" />
              <div class="grow stack tight"><p-skeleton width="50%" height="1rem" /><p-skeleton width="30%" height=".8rem" /></div>
              <p-skeleton width="5rem" height="1.5rem" borderRadius="999px" />
            </div>
          }
        </section>
      }
    }
  `,
  styles: [`
    :host { display: grid; gap: 1rem; }
    .stack { display: grid; gap: .75rem; }
    .stack.tight { gap: .4rem; }
    .lead { display: flex; align-items: center; gap: .75rem; }
    .lead.end { justify-content: flex-end; }
    .grow { flex: 1; }
    .item + .item { padding-top: .75rem; border-top: 1px solid var(--ajh-border); }
    .trow { display: grid; grid-template-columns: var(--cols); gap: 1rem; align-items: center; padding: .35rem 0; }
    .trow.head { padding-bottom: .6rem; border-bottom: 1px solid var(--ajh-border); }
    .metric { display: grid; gap: .5rem; }
    .metrics { grid-template-columns: repeat(auto-fit, minmax(170px, 1fr)); }
  `]
})
export class LoadingSkeletonComponent {
  readonly variant = input<LoadingSkeletonVariant>('list');
  readonly rows = input(4);
  readonly columns = input(4);
  readonly label = input('Loading content');

  protected readonly rowList = computed(() => Array.from({ length: this.variant() === 'dashboard' ? 6 : this.rows() }, (_, i) => i + 1));
  protected readonly colList = computed(() => Array.from({ length: this.columns() }, (_, i) => i + 1));
  protected readonly gridColumns = computed(() => `2fr repeat(${Math.max(this.columns() - 1, 0)}, 1fr)`);
}
