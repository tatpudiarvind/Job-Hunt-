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
  templateUrl: './loading-skeleton.component.html',
  styleUrl: './loading-skeleton.component.scss'
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
