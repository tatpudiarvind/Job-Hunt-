# PrimeNG Full Migration Plan (Option B)

Branch: `Arvind/primeng-full-migration`
Goal: replace Angular Material in `src/ArvindJobHunter.Web` with PrimeNG and keep the current look and behavior.

Work in small chunks. Run one build per chunk with `npx ng build` (about 1 minute).
There is no `development` build configuration in `angular.json`.

## Done

| # | Chunk | Commit |
|---|-------|--------|
| 1 | Shared dialogs, toasts, page header (NotifyService on PrimeNG) | 622701a |
| 2 | Shell: drawer, menu, tooltip | 731a014 |
| 3 | Auth pages and `_primeng-layout.scss` | 5d3fc2e |
| 4 | Basic feature markup (inputs, buttons, cards, icons, progress) | 0b68ff3 |
| 5 | Accordions, tabs, badges, activity table | 7d1f050 |
| 6 | Selects: applications, interview-prep, profile, settings (incl. multiselect) | 82cf6ff |
| 7 | Settings radio group to `p-radiobutton` | 82cf6ff |
| 8 | Jobs/profile button toggles to `p-selectbutton` | 82cf6ff |
| 9 | Jobs paginator to `p-paginator`; removed unused MatMenuModule | 82cf6ff |
| 10 | Build verified (initial bundle 787.68 kB, no errors) | 82cf6ff |
| 11 | Re-scan: no `@angular/material` or `mat-*` left in `src` | 82cf6ff |
| 12 | Material theme and `.mat-*` selectors removed from `styles.scss`; `primeng-layout` import restored | 82cf6ff |
| 13 | `@angular/material` removed from `package.json` (`@angular/cdk` kept for `BreakpointObserver`) | 82cf6ff |
| 14 | Frontend tests: 7 files, 28 tests passed | 82cf6ff |

## Notes

- Builds hung earlier because canceled runs left `node` processes running. If a build hangs, run
  `Get-Process node | Stop-Process -Force` and build again.
- Visual Studio's editor sometimes collapsed the `imports: [...]` line of a component during edits.
  If you see `TS2353 ... does not exist in type 'Component'`, check the component's `imports` array.

## Remaining

- [ ] 15. Visual/runtime check of each page. The dev server compiles cleanly and serves http://localhost:4200
  (verified automatically); clicking through each page still has to be done by hand: login, shell nav, jobs filter and paginator,
  profile filter, settings mode radios and selects, applications status select, dialogs and toasts
- [ ] 16. Optional: push the branch and open a PR
