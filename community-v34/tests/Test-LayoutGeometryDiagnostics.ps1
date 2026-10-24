param([Parameter(Mandatory=$true)][string]$WorkRoot)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'packaging/DiagnosticReport.Common.ps1')
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'packaging/DiagnosticAnalysis.ps1')
if(Test-Path -LiteralPath $WorkRoot){throw 'Fresh output directory required'}
[void][IO.Directory]::CreateDirectory($WorkRoot)
$checks=0
function Check([bool]$Condition,[string]$Name){if(-not $Condition){throw ('FAIL '+$Name)};$script:checks++}
$time='2026-10-24T12:00:00.0000000Z '
foreach($reason in @('frame-count','frame-token','frame-row','frame-shape')){
    $event=Convert-FixLogEvent ($time+'phase=38 elapsedMs=100 code=0 sourcePhase=6 layoutReason='+$reason+' layoutSurfaceKind=frame layoutSupported=0 layoutMainCount=0 layoutActiveMainCount=0 layoutFrameCount=1 layoutActiveFrameCount=1 layoutWorkspaceRowCount=1') 'output/wallpaper-readiness.log'
    Check ($event.layoutReason -ceq $reason) ('frame reason '+$reason)
    Check ($event.layoutSurfaceKind -ceq 'frame' -and $event.layoutFrameCount -eq 1 -and $event.layoutActiveFrameCount -eq 1 -and $event.layoutWorkspaceRowCount -eq 1) 'bounded shape evidence'
    Check ((Get-CodexAnalysisEvent $event) -ceq 'layout-failed') 'shape failure never becomes playback success'
}
$event=Convert-FixLogEvent ($time+'phase=38 elapsedMs=100 code=0 layoutReason=PRIVATE_SENTINEL layoutSurfaceKind=PRIVATE_SENTINEL layoutFrameCount=130 layoutActiveFrameCount=1 layoutActiveFrameCount=1 layoutWorkspaceRowCount=not-a-number title=PRIVATE_SENTINEL') 'output/wallpaper-readiness.log'
Check ($event.layoutReason -ceq 'unknown' -and $event.layoutSurfaceKind -ceq 'unknown') 'unknown shape strings not exported'
Check ($event.layoutFrameCount -eq -1 -and $event.layoutActiveFrameCount -eq -1 -and $event.layoutWorkspaceRowCount -eq -1) 'invalid and duplicate counts remain unknown'
Check (($event|ConvertTo-Json -Compress) -notmatch 'PRIVATE_SENTINEL') 'arbitrary DOM fields excluded'
$base=$time+'Native geometry; phase=1; coverRectKnown=1; targetRectKnown=1; frameKnown=1; targetVisible=1; targetMinimized=0; targetCloaked=0; targetDpi=144; coverDpi=144; coverWidth=1280; coverHeight=720; targetWidth=1268; targetHeight=708; deltaLeft=6; deltaTop=6; deltaRight=-6; deltaBottom=-6; frameInsetLeft=7; frameInsetTop=0; frameInsetRight=7; frameInsetBottom=7'
$event=Convert-FixLogEvent $base 'output/handoff-latest.log'
Check ($null -ne $event -and $event.category -ceq 'native-geometry') 'geometry event recognized'
Check ($event.targetDpi -eq 144 -and $event.coverWidth -eq 1280 -and $event.targetWidth -eq 1268) 'geometry bounded sizes retained'
Check ($event.deltaRight -eq -6 -and $event.frameInsetLeft -eq 7 -and $event.coverRectKnown -eq 1) 'signed relative edges retained'
$event=Convert-FixLogEvent ($time+'Native geometry; phase=1; deltaLeft=-2147483648; deltaTop=-65537; deltaRight=65537; deltaBottom=0 deltaBottom=PRIVATE_SENTINEL; frameInsetLeft=1025; frameInsetTop=-1025; frameInsetRight=NaN; frameInsetBottom=-1; targetDpi=769; coverDpi=47; coverWidth=65537; title=PRIVATE_SENTINEL; path=C:\PRIVATE_SENTINEL; left=123; top=456; pid=789') 'output/handoff-latest.log'
foreach($field in @('deltaLeft','deltaTop','deltaRight','deltaBottom','frameInsetLeft','frameInsetTop','frameInsetRight')){Check ($event.$field -eq -2147483648L) ('unknown relative '+$field)}
Check ($event.frameInsetBottom -eq -1) 'minus one is a valid relative edge'
Check ($event.targetDpi -eq -1 -and $event.coverDpi -eq -1 -and $event.coverWidth -eq -1 -and $event.coverRectKnown -eq -1) 'missing and invalid geometry not success'
$safe=$event|ConvertTo-Json -Compress
Check ($safe -notmatch 'PRIVATE_SENTINEL|"pid"|"left"|"top"|"title"|"path"') 'no absolute coordinate or private fields exported'
$event=Convert-FixLogEvent ($time+'Native geometry; phase=2') 'output/handoff-latest.log'
Check ($event.deltaLeft -eq -2147483648L -and $event.targetRectKnown -eq -1) 'absent geometry stays unknown'
$audit=Convert-FixLogEvent ($time+'phase=45 elapsedMs=100 code=0 layoutAuditFrameCount=1 layoutAuditActiveFrameCount=1 layoutAuditAnyMainCount=0 layoutAuditActiveAnyMainCount=0 layoutAuditPageSurfaceFrameCount=1 layoutAuditLayoutTokenValid=1 layoutAuditPageLayoutTokenValid=1 layoutAuditFrameHashMatches=1 layoutAuditPageSurfaceFlag=1 layoutAuditDirectRowCount=1 layoutAuditDecorationCount=1 layoutAuditDecorationValid=1 layoutAuditComposerCount=0 layoutAuditComposerAmbiguous=0 title=PRIVATE_SENTINEL') 'output/wallpaper-readiness.log'
Check ($audit.layoutAuditFrameCount -eq 1 -and $audit.layoutAuditDecorationValid -eq 1 -and $audit.layoutAuditComposerCount -eq 0) 'phase45 shape evidence'
Check ($null -eq (Get-CodexAnalysisEvent $audit)) 'shape-only does not invent playing status'
Check (($audit|ConvertTo-Json -Compress) -notmatch 'PRIVATE_SENTINEL') 'shape evidence no private string'
$invalid=Convert-FixLogEvent ($time+'phase=45 elapsedMs=100 code=0 layoutAuditFrameCount=130 layoutAuditActiveFrameCount=1 layoutAuditActiveFrameCount=oops layoutAuditLayoutTokenValid=2 layoutAuditComposerCount=1.5 layoutAuditFrameHashMatches=NaN') 'output/wallpaper-readiness.log'
foreach($field in @('layoutAuditFrameCount','layoutAuditActiveFrameCount','layoutAuditLayoutTokenValid','layoutAuditComposerCount','layoutAuditFrameHashMatches')){Check ($invalid.$field -eq -1) ('audit rejects '+$field)}
$candidate=Convert-FixLogEvent ($time+'replay-native-candidates phase=4 verifiedWindows=3; visible=1; hidden=2; cloaked=0; eligible=0; minimized=0; ownedRejected=1; childRejected=0; toolRejected=0; cloakedRejected=0; rectUnavailable=0; sizeRejected=0; stateChanged=0; title=PRIVATE_SENTINEL') 'output/handoff-latest.log'
Check ($candidate.ownedRejected -eq 1 -and $candidate.visible -eq 1 -and $candidate.eligible -eq 0) 'visible rejected candidate explained'
Check (($candidate|ConvertTo-Json -Compress) -notmatch 'PRIVATE_SENTINEL') 'candidate details bounded projection'
$fixture=Join-Path $WorkRoot 'fixture';[void][IO.Directory]::CreateDirectory((Join-Path $fixture 'output'))
$lines=@(1..514|ForEach-Object {$time+'phase=45 elapsedMs='+$_+' code=0 layoutAuditFrameCount=1'})
$lines+='PRIVATE_SENTINEL'
[IO.File]::WriteAllLines((Join-Path $fixture 'output/wallpaper-readiness.log'),$lines,[Text.UTF8Encoding]::new($false))
$tail=Read-FixLogTail $fixture 'output/wallpaper-readiness.log'
Check ($tail.records.Count -eq 512 -and $tail.recordLimitReached -and $tail.discardedRecordCount -eq 2 -and $tail.unrecognizedLineCount -eq 1) 'bounded tail explains dropped records'
Check (($tail|ConvertTo-Json -Depth 8 -Compress) -notmatch 'PRIVATE_SENTINEL') 'tail never includes discarded text'
$report=[pscustomobject]@{logs=@([pscustomobject]@{log='output/wallpaper-readiness.log';records=@($audit);recordLimitReached=$true},[pscustomobject]@{log='output/handoff-latest.log';records=@($candidate,$event)})}
$detail=(Get-CodexDetailedEvidenceAnalysis $report) -join ' '
Check ($detail.Contains('frame=1') -and $detail.Contains('owner/child/tool') -and $detail.Contains('采集上限')) 'human readable detail summary'
$audit2=$audit.PSObject.Copy();$audit2.layoutAuditLayoutTokenValid=0
$report.logs[0].records=@($audit,$audit2)
$detail=(Get-CodexDetailedEvidenceAnalysis $report) -join ' '
Check ($detail.Contains('同一时刻存在差异') -and -not $detail.Contains('frame=1')) 'all fourteen shape fields participate in conflict check'
$report.logs[0].records=@($audit,[pscustomobject]@{utc='2026-10-24T12:01:00Z';category='host-phase';phase=20;code=0})
$detail=(Get-CodexDetailedEvidenceAnalysis $report) -join ' '
Check (-not $detail.Contains('frame=1')) 'new host boundary retires shape from truncated older launch'
$report.logs[0].records=@($audit)
$enumEvent=Convert-FixLogEvent ($time+'phase=45 elapsedMs=100 code=0 sourcePhase=6 layoutReason=frame-token layoutSurfaceKind=frame') 'output/wallpaper-readiness.log'
Check ($enumEvent.layoutReason -ceq 'frame-token' -and $enumEvent.layoutSurfaceKind -ceq 'frame' -and $enumEvent.sourcePhase -eq 6) 'phase45 standalone reason and kind retained'
$enumEvent=Convert-FixLogEvent ($time+'phase=45 elapsedMs=100 code=0 layoutReason=PRIVATE_SENTINEL layoutSurfaceKind=PRIVATE_SENTINEL') 'output/wallpaper-readiness.log'
Check ($enumEvent.layoutReason -ceq 'unknown' -and $enumEvent.layoutSurfaceKind -ceq 'unknown') 'phase45 unknown enum never echoed'
$runtimeShape=Convert-FixLogEvent ($time+'phase=45 elapsedMs=100 code=0 sourcePhase=12 layoutReason=frame-row layoutSurfaceKind=none layoutAuditFrameCount=1 layoutAuditDirectRowCount=0') 'output/wallpaper-readiness.log'
Check ($runtimeShape.sourcePhase -eq 12 -and $runtimeShape.layoutReason -ceq 'frame-row' -and $runtimeShape.layoutAuditDirectRowCount -eq 0) 'post-start route audit survives collection'
Check ($null -eq (Get-CodexAnalysisEvent $runtimeShape)) 'runtime audit not a playback or failure substitute'
$report.logs[1].records+= [pscustomobject]@{utc='2026-10-24T12:01:00Z';category='launcher-stage';stage='launcher-entry'}
$detail=(Get-CodexDetailedEvidenceAnalysis $report) -join ' '
Check (-not $detail.Contains('frame=1') -and -not $detail.Contains('owner/child/tool')) 'earlier run not mislabeled as latest'
$check=[pscustomobject]@{state='current-attempt';passed=$false;failure='file-integrity';failedFileIndex=12;failedFileRole='ambient-media';failureKind='hash-mismatch'}
$text=Get-CodexDiagnosticAnalysis $report $check
Check ($text.Contains('ambient-media') -and $text.Contains('hash-mismatch') -and $text.Contains('=12')) 'current failed check gives component and manifest index'
$check.failedFileRole='PRIVATE_SENTINEL';$check.failureKind='PRIVATE_SENTINEL';$text=Get-CodexDiagnosticAnalysis $report $check
Check (-not $text.Contains('PRIVATE_SENTINEL')) 'analysis refuses untrusted component strings'
$result=[ordered]@{passed=$true;checks=$checks;privateSourceDataUsed=$false;applicationsStarted=$false}
$result|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $WorkRoot 'result.json') -Encoding UTF8
$result|ConvertTo-Json -Compress
