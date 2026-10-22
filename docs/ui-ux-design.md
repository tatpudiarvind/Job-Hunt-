# Job Hunter – UI/UX Redesign

Branch: `Arvind/ui-ux-redesign`

## Problems found (from review and screenshots)

| # | Problem | Where |
|---|---------|-------|
| 1 | Long multi-city locations wrap into a tall column and stretch rows to 400px+ | Jobs table |
| 2 | Action buttons stack vertically (Apply / Re-analyze / icons) and misalign between rows | Jobs table |
| 3 | Search icon renders below the input instead of inside it | Jobs toolbar (`.field-prefix` in a grid `.field`) |
| 3b | Table borders still read `--mat-sys-*` variables that no longer exist after removing Material | Jobs table |
| 4 | Eight dense columns; the important signals (match %, status) are hard to scan | Jobs table |
| 5 | Page header subtitle is a long paragraph; buttons crowd it | All pages |
| 6 | Flat look: same white cards everywhere, weak visual hierarchy | Global |
| 7 | "Invalid PrimeUI License" badge bottom-right | PrimeNG runtime (to investigate separately) |

## Design principles

1. **Scan first, act second.** Each job shows title, company, match score and status at a glance; actions sit in one consistent place.
2. **Cards over wide tables** for rich records (jobs, applications); keep tables only for dense audit data (activity).
3. **Truncate, don't wrap.** Long values show the first item plus a "+N" badge with a tooltip.
4. **One primary action per item.** "Apply" is primary; "Analyze" is secondary; dismiss/open are icon buttons.
5. **Consistent spacing scale:** 4 / 8 / 12 / 16 / 24 / 32 px.
6. **Color with meaning:** green = good match / have skill, amber = medium, red = gap/low, blue = brand/action.

## Visual language

- Background `#f4f6f9`, surfaces white, 14px radius, soft shadow, 1px `#e3e7ee` borders.
- Brand gradient `#1f5fbf → #3b82f6` for logo, page-header accent and match ring.
- Match score shown as a colored ring/pill: ≥70 green, 40–69 amber, <40 red.
- Header: compact title + one-line subtitle; actions right-aligned on the same row.
- Hover lift on interactive cards (`translateY(-1px)` + stronger shadow).

## Page-level design

### Jobs (first)
- Toolbar: search box (icon inside) on the left, filter segmented control on the right, result count.
- Job card row:
  - Left: title (link), `company · first location (+N)` · source · added date, then skill chips (max 4 have + 2 gaps).
  - Middle: status chip.
  - Right: match ring, then actions in one row: `Apply` (primary), `Analyze` (outlined), dismiss icon, open icon.
- Mobile (<720px): actions wrap under the content, full width.

### Shell
- Keep sidebar; tighten nav spacing, add section labels ("Pipeline", "You"), active item with left accent bar.

### Dashboard
- Metric tiles with icon badges and trend color; "Next actions" list first.

### Applications / Approvals / Emails
- Same card pattern as Jobs; status chip + primary action aligned right.

### Settings
- Left in-page navigation of sections; each section a card with clear title and helper text.

## Implementation chunks

1. Global tokens and utilities (spacing, hover lift, search box, score pill/ring, page header compact)
2. Jobs page redesign
3. Shell polish
4. Dashboard
5. Applications, Approvals, Emails
6. Profile, Settings
7. Activity, Interview prep, Help
8. Responsive pass and final review
