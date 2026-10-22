# PrimeNG Full Migration Plan (Option B)

Branch: `Arvind/primeng-full-migration`
Goal: replace Angular Material in `src/ArvindJobHunter.Web` with PrimeNG and keep the current look and behavior.

Work in small chunks. After each chunk, run **one** build with
`npx ng build --configuration development` (much faster than the default production build).

## Done

| # | Chunk | Commit |
|---|-------|--------|
| 1 | Shared dialogs, toasts, page header (NotifyService on PrimeNG) | 622701a |
| 2 | Shell: drawer, menu, tooltip | 731a014 |
| 3 | Auth pages and `_primeng-layout.scss` | 5d3fc2e |
| 4 | Basic feature markup (inputs, buttons, cards, icons, progress) | 0b68ff3 |
| 5 | Accordions, tabs, badges, activity table | 7d1f050 |
| 6 | Selects: applications, interview-prep, profile, settings (incl. multiselect) | uncommitted, build passed |
| 7 | Settings radio group to `p-radiobutton` | uncommitted, not built yet |
| 8 | Jobs/profile button toggles to `p-selectbutton` | uncommitted, not built yet |
| 9 | Jobs `mat-paginator` to `p-paginator`; removed unused MatMenuModule | uncommitted, not built yet |

## Build is hanging (current blocker)

Earlier builds took about 30-60 s. The last few runs were canceled partway through, which probably left
`node`/`ng` processes running. These hold file locks and the `.angular/cache`, so new builds hang with no output.
To fix it, run this in your own terminal:

```powershell
Get-Process node -ErrorAction SilentlyContinue | Stop-Process -Force
cd D:\Job-Hunt-\src\ArvindJobHunter.Web
npx ng build --configuration development
```

If it still hangs, delete `src/ArvindJobHunter.Web/.angular/cache` and build again.

## Remaining

- [ ] 10. Build to verify chunks 7-9, then commit them
- [ ] 11. Re-scan `src/app` for any remaining `@angular/material` imports or `mat-*` tags
- [ ] 12. Remove the Material theme and legacy `.mat-*` selectors from `styles.scss`, then build
- [ ] 13. Remove `@angular/material` (and `@angular/cdk` if unused; note `BreakpointObserver` in shell uses CDK) from `package.json`, then build
- [ ] 14. Run frontend tests (`npx ng test --watch=false`)
- [ ] 15. Do a visual/runtime check of each page, then make the final commit
