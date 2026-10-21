import { TestBed } from '@angular/core/testing';
import { providePrimeNG } from 'primeng/config';
import { LoadingSkeletonComponent } from './loading-skeleton.component';

describe('LoadingSkeletonComponent', () => {
  beforeEach(() => {
    TestBed.configureTestingModule({ imports: [LoadingSkeletonComponent], providers: [providePrimeNG()] });
  });

  function render(inputs: Record<string, unknown>) {
    const fixture = TestBed.createComponent(LoadingSkeletonComponent);
    for (const [key, value] of Object.entries(inputs)) fixture.componentRef.setInput(key, value);
    fixture.detectChanges();
    return fixture.nativeElement as HTMLElement;
  }

  it('announces itself as a busy status region with the given label', () => {
    const el = render({ label: 'Loading jobs' });
    expect(el.getAttribute('role')).toBe('status');
    expect(el.getAttribute('aria-busy')).toBe('true');
    expect(el.getAttribute('aria-label')).toBe('Loading jobs');
  });

  it('renders one placeholder item per requested row in list mode', () => {
    const el = render({ variant: 'list', rows: 3 });
    expect(el.querySelectorAll('.item').length).toBe(3);
  });

  it('renders a header plus one row per requested row in table mode, sized to the column count', () => {
    const el = render({ variant: 'table', rows: 5, columns: 4 });
    const rows = el.querySelectorAll('.trow');
    expect(rows.length).toBe(6);
    expect(rows[0].querySelectorAll('p-skeleton').length).toBe(4);
    expect(el.style.getPropertyValue('--cols')).toBe('2fr repeat(3, 1fr)');
  });

  it('renders six metric placeholders on the dashboard regardless of rows', () => {
    const el = render({ variant: 'dashboard', rows: 2 });
    expect(el.querySelectorAll('.metric').length).toBe(6);
  });
});
