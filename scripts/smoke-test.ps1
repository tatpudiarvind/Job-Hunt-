$ErrorActionPreference = 'Stop'
$base = 'http://localhost:5228'
function J($o) { $o | ConvertTo-Json -Depth 10 -Compress }

Write-Host "health:" (Invoke-RestMethod "$base/health").phase
$status = Invoke-RestMethod "$base/api/auth/status"
Write-Host "configured:" $status.configured
if (-not $status.configured) { Invoke-RestMethod -Method Post "$base/api/auth/setup" -ContentType 'application/json' -Body (J @{username='arvind';password='correct-horse-battery-staple'}) | Out-Null }
$session = Invoke-RestMethod -Method Post "$base/api/auth/login" -ContentType 'application/json' -Body (J @{username='arvind';password='correct-horse-battery-staple'})
$h = @{ Authorization = "Bearer $($session.token)" }
Write-Host "logged in, expires" $session.expiresAt

try { Invoke-RestMethod "$base/api/jobs" | Out-Null; Write-Host "FAIL: unauthenticated jobs call succeeded" } catch { Write-Host "unauth jobs ->" $_.Exception.Response.StatusCode.value__ }

$profile = Invoke-RestMethod "$base/api/candidate-profile" -Headers $h
Write-Host "profile:" $profile.name
$facts = Invoke-RestMethod "$base/api/candidate-profile/facts" -Headers $h
Write-Host "facts:" $facts.Count

$job = Invoke-RestMethod -Method Post "$base/api/jobs" -Headers $h -ContentType 'application/json' -Body (J @{
  title='Senior Software Engineer - .NET & AI'; company='Demo Healthcare GmbH'; location='Germany'; source='MANUAL';
  description='We are looking for a senior engineer with C#, .NET, ASP.NET Core, Azure, microservices and experience with LLMs and RAG. Healthcare / DICOM experience is a plus. Kubernetes nice to have.'
})
Write-Host "job created:" $job.id $job.status

$analyzed = Invoke-RestMethod -Method Post "$base/api/jobs/$($job.id)/analyze" -Headers $h
Write-Host "analyzed status:" $analyzed.job.status "score:" $analyzed.job.match.score "matched:" ($analyzed.job.match.matchedSkills -join ',') "missing:" ($analyzed.job.match.missingSkills -join ',')
Write-Host "tool calls:" ($analyzed.run.toolCalls | ForEach-Object { $_.toolName }) -join ','

$prepared = Invoke-RestMethod -Method Post "$base/api/jobs/$($job.id)/prepare" -Headers $h
Write-Host "resume changes:" $prepared.resume.changes.Count "approval:" $prepared.resumeApproval.id $prepared.resumeApproval.status
Write-Host "cover letter:" $prepared.coverLetter.subject

$apps = Invoke-RestMethod "$base/api/applications" -Headers $h
Write-Host "application status:" $apps[0].status

# Execute before approval -> should be blocked (409)
try { Invoke-RestMethod -Method Post "$base/api/approvals/$($prepared.resumeApproval.id)/execute" -Headers $h -ContentType 'application/json' -Body (J @{idempotencyKey='k1'}) | Out-Null; Write-Host "FAIL: executed without approval" }
catch { Write-Host "execute before approve ->" $_.Exception.Response.StatusCode.value__ }

$approved = Invoke-RestMethod -Method Post "$base/api/approvals/$($prepared.resumeApproval.id)/approve" -Headers $h -ContentType 'application/json' -Body (J @{note='ok'})
Write-Host "approval now:" $approved.status

$receipt = Invoke-RestMethod -Method Post "$base/api/approvals/$($prepared.resumeApproval.id)/execute" -Headers $h -ContentType 'application/json' -Body (J @{idempotencyKey='k1'})
Write-Host "receipt:" $receipt.result $receipt.mode "-" $receipt.message
$receipt2 = Invoke-RestMethod -Method Post "$base/api/approvals/$($prepared.resumeApproval.id)/execute" -Headers $h -ContentType 'application/json' -Body (J @{idempotencyKey='k1'})
Write-Host "idempotent same receipt:" ($receipt.id -eq $receipt2.id)

$dash = Invoke-RestMethod "$base/api/dashboard" -Headers $h
Write-Host "dashboard:" (J $dash)
$audit = Invoke-RestMethod "$base/api/audit-logs" -Headers $h
Write-Host "audit events:" $audit.Count ":" (($audit | ForEach-Object { $_.action } | Select-Object -Unique) -join ',')
Invoke-RestMethod -Method Post "$base/api/auth/logout" -Headers $h | Out-Null
try { Invoke-RestMethod "$base/api/jobs" -Headers $h | Out-Null; Write-Host "FAIL: token still valid after logout" } catch { Write-Host "after logout ->" $_.Exception.Response.StatusCode.value__ }
