# One-off codemod: mechanical Angular Material -> PrimeNG rewrites for inline-template components.
# Usage: powershell -File tools/mat-to-primeng.ps1 <file.ts> [<file.ts> ...]
# Non-mechanical widgets (select, tabs, expansion, table, paginator, button-toggle, radio, badge, card) are left for manual migration.
param([Parameter(Mandatory = $true, ValueFromRemainingArguments = $true)][string[]]$Files)

$ErrorActionPreference = 'Stop'

$moduleMap = [ordered]@{
  'pButton'       = @('ButtonModule', 'primeng/button')
  'pInputText'    = @('InputTextModule', 'primeng/inputtext')
  'pTextarea'     = @('TextareaModule', 'primeng/textarea')
  'pTooltip'      = @('TooltipModule', 'primeng/tooltip')
  'p-progressbar' = @('ProgressBarModule', 'primeng/progressbar')
}

foreach ($file in $Files) {
  $path = (Resolve-Path $file).Path
  $text = [IO.File]::ReadAllText($path)
  $original = $text

  # Form fields -> plain stacked field layout styled globally (.field*)
  $text = [regex]::Replace($text, '<mat-form-field\b[^>]*>', '<div class="field">')
  $text = $text.Replace('</mat-form-field>', '</div>')
  $text = $text.Replace('<mat-label>', '<label class="field-label">').Replace('</mat-label>', '</label>')
  $text = [regex]::Replace($text, '<mat-hint\b[^>]*>', '<small class="field-hint">')
  $text = $text.Replace('</mat-hint>', '</small>')
  $text = [regex]::Replace($text, '<mat-icon\s+matPrefix\s+class="material-symbols-rounded"\s*>', '<span class="material-symbols-rounded field-prefix">')
  $text = [regex]::Replace($text, '(<button\b[^>]*?)\s+matSuffix\b', '$1 class="field-suffix"')

  # Icons -> plain Material Symbols spans
  $text = [regex]::Replace($text, '<mat-icon\b([^>]*)>', [Text.RegularExpressions.MatchEvaluator]{
	  param($m)
	  $attrs = [regex]::Replace($m.Groups[1].Value, '\s+(inline|iconPositionEnd|matListItemIcon)\b(?!=)', '')
	  "<span$attrs>"
	})
  $text = $text.Replace('</mat-icon>', '</span>')

  # Inputs
  $text = [regex]::Replace($text, '(<textarea\b[^>]*?)\bmatInput\b', '${1}pTextarea')
  $text = [regex]::Replace($text, '\bmatInput\b', 'pInputText')

  # Buttons
  $text = [regex]::Replace($text, '\smat-flat-button(?=[\s>])', ' pButton')
  $text = [regex]::Replace($text, '\smat-stroked-button(?=[\s>])', ' pButton outlined')
  $text = [regex]::Replace($text, '\smat-icon-button(?=[\s>])', ' pButton text rounded severity="secondary" data-icon')
  $text = [regex]::Replace($text, '\smat-button(?=[\s>])', ' pButton text')
  $text = [regex]::Replace($text, '\scolor="primary"', '')
  $text = [regex]::Replace($text, '\scolor="warn"', ' severity="danger"')
  $text = [regex]::Replace($text, '\scolor="accent"', ' severity="secondary"')

  # Tooltips
  $text = $text.Replace('matTooltipPosition=', 'tooltipPosition=').Replace('[matTooltip]', '[pTooltip]').Replace('matTooltip=', 'pTooltip=')

  # Progress bars
  $text = [regex]::Replace($text, '<mat-progress-bar\b[^>]*?/>|<mat-progress-bar\b[^>]*>\s*</mat-progress-bar>', '<p-progressbar mode="indeterminate" styleClass="busy-bar" />')

  # Cards -> global .card markup
  $text = [regex]::Replace($text, '<mat-card(?![-\w])([^>]*?)\s*appearance="[^"]*"([^>]*)>', '<mat-card$1$2>')
  $text = [regex]::Replace($text, '<mat-card(?![-\w])(\s+class="([^"]*)")?([^>]*)>', [Text.RegularExpressions.MatchEvaluator]{ param($m) $cls = ('card ' + $m.Groups[2].Value).Trim(); "<section class=""$cls""$($m.Groups[3].Value)>" })
  $text = $text.Replace('</mat-card>', '</section>')
  $text = $text.Replace('<mat-card-header>', '<header class="card-header">').Replace('</mat-card-header>', '</header>')
  $text = $text.Replace('<mat-card-title>', '<h2 class="card-heading">').Replace('</mat-card-title>', '</h2>')
  $text = $text.Replace('<mat-card-subtitle>', '<p class="muted">').Replace('</mat-card-subtitle>', '</p>')
  $text = $text.Replace('<mat-card-content>', '<div class="card-body">').Replace('</mat-card-content>', '</div>')
  $text = [regex]::Replace($text, '<mat-card-actions\b([^>]*)>', [Text.RegularExpressions.MatchEvaluator]{ param($m) $c = [regex]::Match($m.Groups[1].Value, 'class="([^"]*)"'); $cls = ('card-actions ' + $c.Groups[1].Value).Trim(); "<div class=""$cls"">" })
  $text = $text.Replace('</mat-card-actions>', '</div>')
  $text = [regex]::Replace($text, '\bmat-card-header\b', '.card-header')

  # Associate each field label with its control (label for= / control id=)
  $script:fieldIndex = 0
  $text = [regex]::Replace($text, '(?s)<div class="field">.*?</div>', [Text.RegularExpressions.MatchEvaluator]{
      param($m)
      $block = $m.Value
      if ($block -notmatch '<label class="field-label">') { return $block }
      $ctrl = [regex]::Match($block, '<(input|textarea|p-select|p-multiselect)\b[^>]*>')
      if (-not $ctrl.Success) { return $block }
      $tag = $ctrl.Value
      $idMatch = [regex]::Match($tag, '\sid="([^"]+)"')
      if ($idMatch.Success) { $id = $idMatch.Groups[1].Value }
      else {
        $nameMatch = [regex]::Match($tag, '\sname="([^"]+)"')
        $modelMatch = [regex]::Match($tag, '\[\(ngModel\)\]="([^"]+)"')
        $seed = if ($nameMatch.Success) { $nameMatch.Groups[1].Value } elseif ($modelMatch.Success) { $modelMatch.Groups[1].Value } else { "field$($script:fieldIndex)" }
        $script:fieldIndex++
        $id = 'f-' + ([regex]::Replace($seed, '[^A-Za-z0-9]+', '-').Trim('-'))
        $attr = if ($ctrl.Groups[1].Value -like 'p-*') { 'inputId' } else { 'id' }
        $newTag = $tag -replace "^<$($ctrl.Groups[1].Value)\b", "<$($ctrl.Groups[1].Value) $attr=""$id"""
        $block = $block.Replace($tag, $newTag)
      }
      $block.Replace('<label class="field-label">', "<label class=""field-label"" for=""$id"">")
    })

  # Remaining mat-icon references are CSS selectors in component styles
  $text = [regex]::Replace($text, '\bmat-icon\b', '.material-symbols-rounded')

  # Imports: drop Material modules that are no longer referenced in the template
  foreach ($pair in @(
	  @('MatIconModule', 'mat-icon'), @('MatButtonModule', 'mat-(flat-|stroked-|icon-)?button(?!-toggle)'),
	  @('MatFormFieldModule', 'mat-form-field'), @('MatInputModule', 'matInput'),
	  @('MatTooltipModule', 'matTooltip'), @('MatProgressBarModule', 'mat-progress-bar'), @('MatCardModule', 'mat-card'))) {
	if (-not [regex]::IsMatch($text, "<[^>]*$($pair[1])")) {
	  $text = [regex]::Replace($text, "\s*\b$($pair[0])\b,?", [Text.RegularExpressions.MatchEvaluator]{ param($m) if ($m.Value.TrimEnd().EndsWith(',')) { '' } else { '' } })
	}
  }
  $text = [regex]::Replace($text, "(?m)^import \{\s*\}\s*from '@angular/material/[^']+';\r?\n", '')
  $text = [regex]::Replace($text, ',\s*\]', ']')
  $text = [regex]::Replace($text, '\[\s*,\s*', '[')

  # Imports: add PrimeNG modules that are now used
  $toAdd = @()
  foreach ($key in $moduleMap.Keys) {
	$mod = $moduleMap[$key][0]
	if ($text -match [regex]::Escape($key) -and $text -notmatch "\b$mod\b") { $toAdd += , $moduleMap[$key] }
  }
  if ($toAdd.Count -gt 0) {
	$importLines = ($toAdd | ForEach-Object { "import { $($_[0]) } from '$($_[1])';" }) -join "`n"
	$lastImport = [regex]::Matches($text, "(?m)^import [^;]+;\r?\n") | Select-Object -Last 1
	$text = $text.Insert($lastImport.Index + $lastImport.Length, "$importLines`n")
	$names = ($toAdd | ForEach-Object { $_[0] }) -join ', '
	$text = [regex]::Replace($text, '(\n\s*imports:\s*\[)', "`$1$names, ", 1)
  }

  if ($text -ne $original) {
	[IO.File]::WriteAllText($path, $text)
	Write-Host "updated $file"
  }
}
