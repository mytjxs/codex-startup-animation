[CmdletBinding()]
param([string]$ToolsRoot,[string]$WorkRoot,[switch]$ToolsProbeOnly,[string]$BaselineToolsRoot)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$testDirectory=[IO.Path]::GetDirectoryName($PSCommandPath)
if(-not $PSBoundParameters.ContainsKey('ToolsRoot')){
    $parent=[IO.Path]::GetDirectoryName($testDirectory)
    $ToolsRoot=if(Test-Path -LiteralPath (Join-Path $parent 'DiagnosticReport.Common.ps1')){$parent}else{Join-Path $parent 'packaging'}
}
. (Join-Path $ToolsRoot 'DiagnosticReport.Common.ps1')
Assert-FixPowerShell
foreach($name in @('Collect-DiagnosticReport.ps1','DiagnosticReport.Common.ps1')){
    $bytes=[IO.File]::ReadAllBytes((Join-Path $ToolsRoot $name))
    if($bytes.Length -lt 3 -or $bytes[0] -ne 239 -or $bytes[1] -ne 187 -or $bytes[2] -ne 191){throw 'TEST FAILED: UTF-8 BOM required'}
}
if($ToolsProbeOnly){Write-Output 'PASS portable tools discovery and BOM';exit 0}
if(-not $PSBoundParameters.ContainsKey('WorkRoot')){$WorkRoot=Join-Path ([IO.Path]::GetDirectoryName($ToolsRoot)) ('portable-report-tests\'+[Guid]::NewGuid().ToString('N').Substring(0,8))}
[void][IO.Directory]::CreateDirectory($WorkRoot)
$WorkRoot=Assert-FixPath $WorkRoot -Directory
$powerShell51=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
$clock=[Diagnostics.Stopwatch]::StartNew();$script:passed=0
function Check([bool]$Condition,[string]$Name){if(-not $Condition){throw ('TEST FAILED: '+$Name)}}
function Save-Text([string]$Path,[string]$Text){[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Path));[IO.File]::WriteAllText($Path,$Text,[Text.UTF8Encoding]::new($false))}
function Save-Json([string]$Path,$Value){Save-Text $Path ($Value|ConvertTo-Json -Depth 8)}
function Record([string]$Root,[string]$Name){$path=Join-Path $Root $Name;return [pscustomobject]@{path=$Name;bytes=(Get-Item -LiteralPath $path).Length;sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash}}
function New-SmallManifest([string]$Name){
    $root=Join-Path $WorkRoot $Name;[void][IO.Directory]::CreateDirectory($root)
    $manifest=[pscustomobject]@{schemaVersion=1;product='CodexDragonPublic';files=@([pscustomobject]@{path='Launch.exe';bytes=0;sha256=('A'*64)})}
    Save-Json (Join-Path $root 'release-manifest.json') $manifest
    return [pscustomobject]@{root=$root;manifest=$manifest}
}
function Reject([string]$Name,[scriptblock]$Action,[string]$Code){
    $caught=$false;try{& $Action|Out-Null}catch{$caught=$true;if($Code){Check ($_.Exception.Message -ceq ('FCP-'+$Code)) ($Name+' bounded error')}}
    Check $caught ($Name+' rejection');$script:passed++;Write-Output ('PASS '+$Name)
}

$root=Join-Path $WorkRoot '完整便携包 空格';[void][IO.Directory]::CreateDirectory($root)
$files=@();for($i=0;$i -lt 94;$i++){$name=if($i -eq 0){'Launch.exe'}else{'files/item-'+$i.ToString('000')+'.txt'};Save-Text (Join-Path $root $name) ('synthetic bytes '+$i);$files+=Record $root $name}
foreach($name in @('Collect-DiagnosticReport.ps1','DiagnosticReport.Common.ps1')){Copy-Item -LiteralPath (Join-Path $ToolsRoot $name) -Destination (Join-Path $root $name);$files+=Record $root $name}
Save-Json (Join-Path $root 'release-manifest.json') ([ordered]@{schemaVersion=1;product='CodexDragonPublic';files=$files})
Save-Text (Join-Path $root 'wallpaper-host-config.json') 'PRIVATE runtime settings'
Save-Text (Join-Path $root 'output/wallpaper-readiness.log') (@(
    '2026-10-18T12:00:00Z phase=37 elapsedMs=1500 code=137 sourcePhase=6 observationKind=2 requestBudgetMs=1500 timeoutStage=3 bytesReceived=8192 framesReceived=9 matchedResponses=8 lateResponses=1 pid=1234 trace=SECRET',
    '2026-10-18T12:00:01Z phase=38 elapsedMs=2000 code=0 sourcePhase=6 rootReady=1 bodyReady=1 layoutSupported=0 documentHidden=1 attached=0 mediaReady=-1 prepared=-1 disposed=0 firstFrameConfirmed=-1 generationMatches=-1 layoutWindowElectron=1 layoutMainCount=129 layoutActiveMainCount=0 URL=https://private.example',
    '2026-10-18T12:00:02Z phase=38 elapsedMs=2500 code=0 sourcePhase=10 rootReady=PRIVATE attached=1 attached=0 layoutMainCount=130 layoutActiveMainCount=-1',
    '2026-99-99T25:60:60Z phase=38 elapsedMs=2 code=0 rootReady=1'
) -join "`n")
Save-Text (Join-Path $root 'output/handoff-latest.log') (@('2026-10-18T12:00:00Z startup-stage=launcher-entry pid=1234 elapsedMs=0','2026-10-18T12:00:01Z Living media opened intro; duration=1.250','2026-10-18T12:00:02Z Living preparation failed: 壁纸准备失败 本次没有播放成功转场','2026-10-18T12:00:03Z Cleanup complete: decoder, local pulse and timers released; no desktop shield') -join "`n")
Save-Text (Join-Path $root 'diagnostics/portable-launch.log') (@('2026-10-18T12:00:00Z PUBLIC_PREPARATION_TIMEOUT','2026-10-18T12:00:01Z PUBLIC_PRIVATE_SECRET') -join "`n")
Save-Text (Join-Path $root 'output/private-unlisted.log') '2026-10-18T12:00:00Z phase=37 elapsedMs=1 code=65000 SECRET'
Check (-not (Test-Path -LiteralPath (Join-Path $root 'test-copy-receipt.json'))) 'no receipt fixture'
$before=@{};Get-ChildItem -LiteralPath $root -File -Recurse|ForEach-Object {$_.IsReadOnly=$true;$before[$_.FullName]=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
$runClock=[Diagnostics.Stopwatch]::StartNew()
Push-Location -LiteralPath $WorkRoot
try{$output=@(& $powerShell51 -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'Collect-DiagnosticReport.ps1') 2>&1);$exit=$LASTEXITCODE}finally{Pop-Location}
$runMs=$runClock.ElapsedMilliseconds
Check ($exit -eq 0) ('no-argument wrapper: '+($output -join ' '))
$reports=@(Get-ChildItem -LiteralPath (Join-Path $root 'CodexDiagnosticReports') -Filter '*.zip')
Check ($reports.Count -eq 1) 'one report ZIP'
Add-Type -AssemblyName System.IO.Compression.FileSystem
$zip=[IO.Compression.ZipFile]::OpenRead($reports[0].FullName)
try{Check ($zip.Entries.Count -eq 1 -and $zip.Entries[0].FullName -ceq 'numeric-report.json') 'single sanitized ZIP entry';$reader=[IO.StreamReader]::new($zip.Entries[0].Open());try{$json=$reader.ReadToEnd()}finally{$reader.Dispose()}}finally{$zip.Dispose()}
$report=$json|ConvertFrom-Json
Check ($report.kind -ceq 'CodexDragonStartupFixReport' -and $report.declaredFileCount -eq 96 -and $report.verifiedFileCountAtPreparation -eq -1) 'declared count is not a verification claim'
Check ($report.targetManifestSha256 -ceq (Get-FileHash -LiteralPath (Join-Path $root 'release-manifest.json') -Algorithm SHA256).Hash) 'manifest identity hashes same bytes'
Check ($report.logs.Count -eq 6 -and -not $report.rawLogsIncluded -and $json -notmatch 'PRIVATE|SECRET|private\.example|65000|"pid"|"trace"') 'privacy and fixed six-log allowlist'
$events=@($report.logs|Where-Object {$_.log -ceq 'output/wallpaper-readiness.log'})[0].records
Check ($events.Count -eq 3 -and $events[0].phase -eq 37 -and $events[0].sourcePhase -eq 6 -and $events[0].observationKind -eq 2 -and $events[0].lateResponses -eq 1) 'phase37 request evidence'
Check ($events[1].phase -eq 38 -and $events[1].layoutMainCount -eq 129 -and $events[1].layoutWindowElectron -eq 1 -and $events[1].generationMatches -eq -1) 'phase38 valid state'
foreach($key in @('rootReady','bodyReady','layoutSupported','documentHidden','attached','mediaReady','prepared','disposed','firstFrameConfirmed','generationMatches','layoutWindowElectron','layoutMainCount','layoutActiveMainCount')){Check ($events[2].$key -eq -1) 'phase38 missing or malformed state stays unknown'}
foreach($path in $before.Keys){Check ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ceq $before[$path] -and (Get-Item -LiteralPath $path).IsReadOnly) 'input bytes and readonly attributes unchanged'}
Check ((Get-ChildItem -LiteralPath $root -File -Recurse).Count -eq $before.Count+1) 'only ZIP file added'
$script:passed++;Write-Output ('PASS complete no-receipt96-file manifest; noarg PS5.1 Chinese/spaces/unrelatedcwd; phase37/38privacy; input unchanged; runtimeMs='+$runMs)

$stamp='2026-10-18T12:02:00Z '
$rotation=$stamp+'phase=35 elapsedMs=25000 pid=1234 trace='+('a'*32)+' activeClip=particles completedCycles=2 alternateAvailable=1 alternateFailed=0'
$booleanNames=@('attached','mediaReady','prepared','disposed','playing','wallpaperApplied','revealed','layoutSupported','firstFrameConfirmed','documentHidden','stageVisible','recoveryNeeded','leaseNeedsRenew','layoutSuspended','memoryPressure','manuallyPaused','videoPaused','videoSeeking','playStarted','frameCallbackPending','preparationTimerPending','frozen')
$stateFields=(@($booleanNames|ForEach-Object {$_+'=1'}) -join ' ')+' videoReadyState=4 videoErrorCode=0 videoNetworkState=2 mediaTimeMs=1200 mediaGeneration=3 presentedFrames=144 protocolVersion=2 generationMatches=1 rendererCode=playing actualPhase=visible health=fresh'
$snapshot=$stamp+'phase=10 snapshotKind=1 ordinal=3 elapsedMs=21000 sampleAgeMs=80 pid=1234 trace='+('b'*32)+' '+$stateFields
$runtime=$stamp+'phase=39 elapsedMs=25000 code=0 pid=1234 trace='+('c'*32)+' '+$stateFields+' commitAccepted=1'
$quality=$stamp+'phase=40 elapsedMs=25000 code=0 pid=1234 trace='+('d'*32)+' videoTotalFrames=250 videoDroppedFrames=12 videoCorruptedFrames=0 videoWidth=2560 videoHeight=1440 mediaGeneration=3 presentedFrames=144'
if($BaselineToolsRoot){
    $baselineCommon=Assert-FixPath (Join-Path $BaselineToolsRoot 'DiagnosticReport.Common.ps1') -File
    $oldRotation=& {param($source,$line) . $source;Convert-FixLogEvent $line 'output/wallpaper-readiness.log'} $baselineCommon $rotation
    $oldSnapshot=& {param($source,$line) . $source;Convert-FixLogEvent $line 'output/wallpaper-readiness.log'} $baselineCommon $snapshot
    Check ($null -eq $oldRotation) 'baseline reproduces dropped rotation'
    Check ($null -ne $oldSnapshot -and $null -eq $oldSnapshot.PSObject.Properties['playing']) 'baseline reproduces stripped snapshot state'
    $script:passed++;Write-Output 'PASS original collector regressions independently reproduced'
}
$r=Convert-FixLogEvent $rotation 'output/wallpaper-readiness.log'
Check ($r.phase -eq 35 -and $r.activeClip -ceq 'particles' -and $r.completedCycles -eq 2 -and $r.alternateAvailable -eq 1 -and $r.alternateFailed -eq 0) 'code-less rotation retained'
$s=Convert-FixLogEvent $snapshot 'output/wallpaper-readiness.log'
Check ($s.snapshotKind -eq 1 -and $s.ordinal -eq 3 -and $s.sampleAgeMs -eq 80 -and $s.videoReadyState -eq 4 -and $s.mediaTimeMs -eq 1200 -and $s.rendererCode -ceq 'playing' -and $s.actualPhase -ceq 'visible' -and $s.health -ceq 'fresh') 'preparation snapshot complete'
foreach($key in $booleanNames){Check ($s.$key -eq 1) ('snapshot boolean '+$key)}
$s=Convert-FixLogEvent $runtime 'output/wallpaper-readiness.log'
foreach($key in ($booleanNames+@('generationMatches','commitAccepted'))){Check ($s.$key -eq 1) ('runtime boolean '+$key)}
$q=Convert-FixLogEvent $quality 'output/wallpaper-readiness.log'
Check ($q.videoTotalFrames -eq 250 -and $q.videoDroppedFrames -eq 12 -and $q.videoCorruptedFrames -eq 0 -and $q.videoWidth -eq 2560 -and $q.videoHeight -eq 1440 -and $q.mediaGeneration -eq 3 -and $q.presentedFrames -eq 144) 'playback quality numerical counters'
$script:passed++;Write-Output 'PASS phases10/35/39/40 complete safe fields'

$requestFailure=$stamp+'phase=41 elapsedMs=27000 code=137 pid=1234 trace='+('f'*32)+' operationKind=2 requestElapsedMs=750 requestBudgetMs=750 timeoutStage=3'
foreach($kind in 1..4){
    $r=Convert-FixLogEvent ($requestFailure.Replace('operationKind=2',('operationKind='+$kind))) 'output/wallpaper-readiness.log'
    Check ($r.operationKind -eq $kind -and $r.requestElapsedMs -eq 750 -and $r.requestBudgetMs -eq 750 -and $r.timeoutStage -eq 3 -and $r.code -eq 137) 'runtime failure operation and deadline evidence retained'
}
$r=Convert-FixLogEvent ($stamp+'phase=41 elapsedMs=27000 code=172 operationKind=4 requestElapsedMs=0 requestBudgetMs=1 timeoutStage=0') 'output/wallpaper-readiness.log'
Check ($r.operationKind -eq 4 -and $r.requestElapsedMs -eq 0 -and $r.requestBudgetMs -eq 1 -and $r.timeoutStage -eq 0) 'non-timeout runtime failure preserves zero stage and minimum budget'
foreach($line in @(
    ($requestFailure+' operationKind=PRIVATE requestElapsedMs=PRIVATE requestBudgetMs=PRIVATE timeoutStage=PRIVATE'),
    ($stamp+'phase=41 elapsedMs=27000 code=137 operationKind=5 requestElapsedMs=31536000001 requestBudgetMs=751 timeoutStage=4'),
    ($stamp+'phase=41 elapsedMs=27000 code=137 operationKind=0 requestElapsedMs=-2 requestBudgetMs=0 timeoutStage=-2'),
    ($stamp+'phase=41 elapsedMs=27000 code=137'))){
    $r=Convert-FixLogEvent $line 'output/wallpaper-readiness.log'
    foreach($key in @('operationKind','requestElapsedMs','requestBudgetMs','timeoutStage')){Check ($r.$key -eq -1) ('runtime failure missing/malformed/duplicate stays unknown '+$key)}
}
$r=Convert-FixLogEvent ($stamp+'phase=12 elapsedMs=27000 code=137 operationKind=2 requestElapsedMs=750') 'output/wallpaper-readiness.log'
Check ($null -eq $r.PSObject.Properties['operationKind'] -and $null -eq $r.PSObject.Properties['requestElapsedMs']) 'new detail fields accepted only for exact phase41'
$script:passed++;Write-Output 'PASS phase41 bounded failure-only request evidence and unknown fields'

foreach($pressureState in @('suspended','resume-requested')){
    $line=$stamp+'phase=43 elapsedMs=28000 code=0 pressureState='+$pressureState+' availableCommitBytes=536870911 totalCommitBytes=34359738368'
    $r=Convert-FixLogEvent $line 'output/wallpaper-readiness.log'
    Check ($r.pressureState -ceq $pressureState -and $r.availableCommitBytes -eq 536870911 -and $r.totalCommitBytes -eq 34359738368) 'recoverable host commit-pressure event retains fixed state and optional counters'
}
$r=Convert-FixLogEvent ($stamp+'phase=43 elapsedMs=28000 code=0 pressureState=suspended availableCommitBytes=281474976710656 totalCommitBytes=281474976710656') 'output/wallpaper-readiness.log'
Check ($r.availableCommitBytes -eq 281474976710656 -and $r.totalCommitBytes -eq 281474976710656) 'declared 2^48 counter maximum survives export without truncation'
foreach($suffix in @('', ' pressureState=PRIVATE availableCommitBytes=PRIVATE totalCommitBytes=PRIVATE',' pressureState=suspended pressureState=resume-requested availableCommitBytes=1 availableCommitBytes=2',' pressureState=playing availableCommitBytes=-2 totalCommitBytes=281474976710657')){
    $r=Convert-FixLogEvent ($stamp+'phase=43 elapsedMs=28000 code=0'+$suffix) 'output/wallpaper-readiness.log'
    Check ($r.pressureState -ceq 'unknown' -and $r.availableCommitBytes -eq -1 -and $r.totalCommitBytes -eq -1 -and ($r|ConvertTo-Json -Compress) -cnotmatch 'PRIVATE') 'ambiguous host pressure state/counters remain unknown'
}
foreach($header in @('phase=42 elapsedMs=28000 code=0','phase=43 elapsedMs=28000 code=174')){
    $r=Convert-FixLogEvent ($stamp+$header+' pressureState=suspended availableCommitBytes=123') 'output/wallpaper-readiness.log'
    Check ($null -eq $r.PSObject.Properties['pressureState'] -and $null -eq $r.PSObject.Properties['availableCommitBytes']) 'host pressure details accepted only at exact nonfailure phase43'
}
$script:passed++;Write-Output 'PASS recoverable host commit-pressure allowlist and unknown boundaries'

$gates=$stamp+'Living preparation gates; elapsedMs=25000; nativeStable=True; focusSettled=False; targetForeground=False; ownOverlayActive=True; foreground=film; filmWpfActive=True; filmNativeForeground=True; pulseWpfActive=False; pulseNativeForeground=False; focusAttempts=2; wallpaperPrepared=True; pulseReady=True; finaleReady=True; mediaCode=1; refreshCode=1'
$finale=$stamp+'Living finale committed; nativeStable=True; focusSettled=True; actualForeground=False; wallpaperReady=True; finalePrerolled=True; pulseReady=True; speed=1'
$complete=$stamp+'Handoff complete; success=True; pulseFrames=46; wallpaperCommitConfirmed=True; wallpaperPlaybackRequiresObserver=True; replayExisting=False'
$bridge=$stamp+'bridge-diagnostic trace='+('e'*32)+' pid=1234 operation=open phase=7 elapsedMs=100 reason=0 outcome=bridge-started exceptionType=None hresult=0 hostPid=5678 disposition=2 mediaCode=1'
$g=Convert-FixLogEvent $gates 'output/handoff-latest.log';Check ($g.category -ceq 'living-preparation-gates' -and $g.nativeStable -eq 1 -and $g.focusSettled -eq 0 -and $g.foreground -ceq 'film' -and $g.focusAttempts -eq 2 -and $g.wallpaperPrepared -eq 1) 'gates retained'
$g=Convert-FixLogEvent $finale 'output/handoff-latest.log';Check ($g.category -ceq 'living-finale-committed' -and $g.wallpaperReady -eq 1 -and $g.existingWallpaperPreserved -eq -1 -and $g.actualForeground -eq 0 -and $g.speed -eq 1) 'finale branch absent stays unknown'
$g=Convert-FixLogEvent $complete 'output/handoff-latest.log';Check ($g.category -ceq 'handoff-complete' -and $g.success -eq 1 -and $g.pulseFrames -eq 46 -and $g.wallpaperCommitConfirmed -eq 1 -and $g.replayExisting -eq 0) 'handoff completion retained'
$g=Convert-FixLogEvent ($stamp+'Pulse reveal begins; targetReady=True; focusPrepared=True; waitSeconds=0.150') 'output/handoff-latest.log';Check ($g.category -ceq 'pulse-reveal-begins' -and $g.waitMs -eq 150) 'pulse reveal fixed decimal'
$g=Convert-FixLogEvent ($stamp+'Opaque pulse pre-rendered behind playing movie; coverCycles=2; filmPosition=1.250') 'output/handoff-latest.log';Check ($g.coverCycles -eq 2 -and $g.filmPositionMs -eq 1250) 'pulse prerender'
$g=Convert-FixLogEvent ($stamp+'Living wallpaper preparation evidence=decoded-frame-confirmed') 'output/handoff-latest.log';Check ($g.evidence -ceq 'decoded-frame-confirmed') 'preparation evidence enum'
$g=Convert-FixLogEvent $bridge 'output/handoff-latest.log';Check ($g.operation -ceq 'open' -and $g.outcome -ceq 'bridge-started' -and $g.disposition -eq 2 -and $g.mediaCode -eq 1) 'bridge safe state'
$script:passed++;Write-Output 'PASS fixed handoff/gate/finale/pulse/bridge categories'

$resource=$stamp+'Startup resource sample; elapsedMs=25000; intervalMs=2000; cpuPermille=750; privateBytes=123456789; availablePhysicalBytes=8589934592; totalPhysicalBytes=17179869184; availableCommitBytes=4294967296; totalCommitBytes=34359738368; logicalProcessors=16; lowMemory=0'
foreach($resourceReason in @('system-memory-pressure','system-commit-pressure','wallpaper-commit-pressure','startup-private-memory-limit','startup-sustained-cpu-limit','startup-resource-monitor-unavailable')){
    $g=Convert-FixLogEvent ($stamp+'Startup resource protection: '+$resourceReason) 'output/handoff-latest.log'
    Check ($null -ne $g -and $g.category -ceq 'startup-resource-protection' -and $g.resourceReason -ceq $resourceReason) 'exact resource trip reason retained'
}
$g=Convert-FixLogEvent ($stamp+'Startup resource protection: system-memory-pressure PRIVATE C:\Users\SECRET') 'output/handoff-latest.log'
Check ($g.resourceReason -ceq 'unknown' -and ($g|ConvertTo-Json -Compress) -notmatch 'PRIVATE|SECRET|C:\\Users') 'unknown resource reason never exported'
$g=Convert-FixLogEvent ($stamp+'Startup animation released for resource protection; wallpaper preparation continues; no success finale') 'output/handoff-latest.log'
Check ($g.category -ceq 'startup-resource-animation-released') 'exact resource compensation event retained'
Check ($null -eq (Convert-FixLogEvent ($stamp+'Startup animation released for resource protection; wallpaper preparation continues; no success finale PRIVATE') 'output/handoff-latest.log')) 'resource compensation text suffix not accepted'
$script:passed++;Write-Output 'PASS exact bounded startup resource reasons'
$g=Convert-FixLogEvent $resource 'output/handoff-latest.log';Check ($g.category -ceq 'startup-resource-sample' -and $g.cpuPermille -eq 750 -and $g.availablePhysicalBytes -eq 8589934592 -and $g.logicalProcessors -eq 16 -and $g.lowMemory -eq 0) 'startup numeric resource sample'
Check ($g.notificationLow -eq -1) 'legacy startup sample preserves absent physical notification as unknown'
foreach($notification in @(-1,0,1)){
    $g=Convert-FixLogEvent ($resource+'; notificationLow='+$notification) 'output/handoff-latest.log'
    Check ($g.notificationLow -eq $notification -and $g.lowMemory -eq 0) 'physical notification remains independent of protection input'
}
foreach($notification in @('PRIVATE','2','-2','true','1.5','1; notificationLow=0')){
    $g=Convert-FixLogEvent ($resource+'; notificationLow='+$notification) 'output/handoff-latest.log'
    Check ($g.notificationLow -eq -1 -and ($g|ConvertTo-Json -Compress) -cnotmatch 'PRIVATE') 'notification malformed or duplicated remains unknown'
}
$badResource=$stamp+'Startup resource sample; elapsedMs=-2; intervalMs=PRIVATE; cpuPermille=1001; privateBytes=281474976710657; availablePhysicalBytes=1 availablePhysicalBytes=PRIVATE; totalPhysicalBytes=PRIVATE; availableCommitBytes=-2; totalCommitBytes=1.5; logicalProcessors=4097; lowMemory=True'
$g=Convert-FixLogEvent $badResource 'output/handoff-latest.log'
foreach($key in @('elapsedMs','intervalMs','cpuPermille','privateBytes','availablePhysicalBytes','totalPhysicalBytes','availableCommitBytes','totalCommitBytes','logicalProcessors','lowMemory')){Check ($g.$key -eq -1) ('invalid resource value remains unknown '+$key)}
$script:passed++;Write-Output 'PASS bounded startup resource fields and ambiguous unknowns'

$nativeBody='Native readiness waiting after placement; phase=1; elapsedMs=25000; exists=True; verified=True; visible=True; minimized=False; cloaked=False; mainCandidate=True; sameProbe=True; probeAgeMs=750; probeResponsive=True; probePending=True; postReadyLossMs=0; idle animation continues'
$nativeWait=$stamp+$nativeBody;$compactWait=$stamp+$nativeBody.Replace('Native readiness waiting after placement; ','Compact placement waiting; ').Replace('phase=1','phase=2')
$compactPhaseZero=$compactWait.Replace('phase=2','phase=0').Replace('elapsedMs=25000','elapsedMs=63500').Replace('sameProbe=True','sameProbe=False').Replace('probeAgeMs=750','probeAgeMs=-1').Replace('probeResponsive=True','probeResponsive=False').Replace('probePending=True','probePending=False')
$g=Convert-FixLogEvent $compactPhaseZero 'output/handoff-latest.log';Check ($g.category -ceq 'compact-placement-wait' -and $g.phase -eq 0 -and $g.elapsedMs -eq 63500 -and $g.visible -eq 1 -and $g.verified -eq 1 -and $g.sameProbe -eq 0 -and $g.probePending -eq 0 -and $g.probeAgeMs -eq -1) 'reported phase0 visible hide-ack wait is retained exactly'
foreach($case in @(@($nativeWait,'native-readiness-wait',1),@($compactWait,'compact-placement-wait',2))){
    $g=Convert-FixLogEvent $case[0] 'output/handoff-latest.log'
    Check ($g.category -ceq $case[1] -and $g.phase -eq $case[2] -and $g.elapsedMs -eq 25000 -and $g.probeAgeMs -eq 750 -and $g.postReadyLossMs -eq 0) 'native wait fixed counters'
    foreach($key in @('exists','verified','visible','mainCandidate','sameProbe','probeResponsive','probePending')){Check ($g.$key -eq 1) ('native wait true '+$key)}
    foreach($key in @('minimized','cloaked')){Check ($g.$key -eq 0) ('native wait false '+$key)}
}
$badNative=$nativeWait+' phase=PRIVATE elapsedMs=PRIVATE exists=PRIVATE verified=PRIVATE visible=PRIVATE minimized=PRIVATE cloaked=PRIVATE mainCandidate=PRIVATE sameProbe=PRIVATE probeAgeMs=PRIVATE probeResponsive=PRIVATE probePending=PRIVATE postReadyLossMs=PRIVATE'
$g=Convert-FixLogEvent $badNative 'output/handoff-latest.log'
foreach($key in @('phase','elapsedMs','exists','verified','visible','minimized','cloaked','mainCandidate','sameProbe','probeAgeMs','probeResponsive','probePending','postReadyLossMs')){Check ($g.$key -eq -1) ('native malformed duplicate unknown '+$key)}
$g=Convert-FixLogEvent ($stamp+'Compact placement waiting; phase=3; elapsedMs=31536000001; probeAgeMs=-2; postReadyLossMs=99999999999999999; exists=true; probePending=1; nativeReadiness=unavailable; idle animation continues') 'output/handoff-latest.log'
foreach($key in @('phase','elapsedMs','exists','verified','visible','minimized','cloaked','mainCandidate','sameProbe','probeAgeMs','probeResponsive','probePending','postReadyLossMs')){Check ($g.$key -eq -1) ('native absent/out-of-bound unknown '+$key)}
Check ($null -eq (Convert-FixLogEvent ($stamp+'PRIVATE Native readiness waiting after placement; phase=1') 'output/handoff-latest.log')) 'unrelated native wait prefix rejected'
$script:passed++;Write-Output 'PASS native wait diagnostics preserve exact structural evidence without free text'

foreach($command in @('reveal','pause','disable','status','refresh','prepare','commit')){
    $g=Convert-FixLogEvent ($stamp+'Wallpaper bridge command failed: '+$command) 'output/handoff-latest.log'
    Check ($null -ne $g -and $g.category -ceq 'wallpaper-command-failed' -and $g.command -ceq $command) 'fixed queued-command failure retained'
}
Check ($null -eq (Convert-FixLogEvent ($stamp+'Wallpaper bridge command failed: reveal PRIVATE') 'output/handoff-latest.log')) 'command failure arbitrary suffix not exported'
$g=Convert-FixLogEvent ($stamp+'Native recovery still unconfirmed after bounded retry') 'output/handoff-latest.log'
Check ($null -ne $g -and $g.category -ceq 'native-recovery-unconfirmed') 'native recovery remains unconfirmed'
$script:passed++;Write-Output 'PASS bounded recovery diagnostics distinguish request failure from confirmation'

$failureCases=@(
    @('客户端窗口准备失败 已保留 Codex 请查看启动日志','client-window-preparation-failed'),
    @('壁纸宿主已退出 本次结束启动动画并保留客户端','wallpaper-host-exited'),
    @('壁纸准备失败 本次没有播放成功转场','wallpaper-preparation-failed'),
    @('启动准备失败 已保留 Codex 请查看启动日志','startup-preparation-failed'),
    @('等待客户端或壁纸准备超时 本次没有播放成功转场','startup-preparation-timeout'),
    @('准备证据已过期 本次普通显示 Codex','preparation-evidence-expired'),
    @('未找到可用的壁纸会话 本次没有播放成功转场','wallpaper-session-unavailable'),
    @('启动资源保护已触发 本次停止动画并保留 Codex','startup-resource-protection'),
    @('转场前客户端或壁纸状态发生变化 本次没有播放成功转场','handoff-state-changed'))
foreach($case in $failureCases){$g=Convert-FixLogEvent ($stamp+'Living preparation failed: '+$case[0]) 'output/handoff-latest.log';Check ($g.category -ceq $case[1] -and $g.reason -ceq $case[1]) 'fixed failure reason preserved'}
$g=Convert-FixLogEvent ($stamp+'Living preparation failed: PRIVATE arbitrary error C:\Users\PRIVATE API_KEY=PRIVATE') 'output/handoff-latest.log'
Check ($g.category -ceq 'living-preparation-failed' -and $g.reason -ceq 'unknown' -and ($g|ConvertTo-Json -Compress) -notmatch 'PRIVATE|API_KEY|Users') 'unknown failure never exports error text'
$script:passed++;Write-Output 'PASS allowlisted failure reasons and unknown future failure safety'

$invalidRuntime=$stamp+'phase=39 elapsedMs=25000 code=0 '+(@($booleanNames|ForEach-Object {$_+'=1 '+$_+'=PRIVATE'}) -join ' ')+' generationMatches=2 commitAccepted=False videoReadyState=5 videoErrorCode=-2 videoNetworkState=4 mediaTimeMs=86400001 mediaGeneration=2147483648 presentedFrames=1.5 protocolVersion=3 rendererCode=playing rendererCode=PRIVATE actualPhase=PRIVATE health=fresh health=PRIVATE'
$s=Convert-FixLogEvent $invalidRuntime 'output/wallpaper-readiness.log'
foreach($key in ($booleanNames+@('generationMatches','commitAccepted','videoReadyState','videoErrorCode','videoNetworkState','mediaTimeMs','mediaGeneration','presentedFrames','protocolVersion'))){Check ($s.$key -eq -1) ('invalid/duplicate state unknown '+$key)}
foreach($key in @('rendererCode','actualPhase','health')){Check ($s.$key -ceq 'unknown') ('invalid/duplicate enum unknown '+$key)}
$q=Convert-FixLogEvent ($stamp+'phase=40 elapsedMs=25000 code=0 videoTotalFrames=2147483648 videoDroppedFrames=1 videoDroppedFrames=PRIVATE videoCorruptedFrames=-2 videoWidth=16385 videoHeight=PRIVATE mediaGeneration=3.5 presentedFrames=999999999999999999999999') 'output/wallpaper-readiness.log'
foreach($key in @('videoTotalFrames','videoDroppedFrames','videoCorruptedFrames','videoWidth','videoHeight','mediaGeneration','presentedFrames')){Check ($q.$key -eq -1) ('quality invalid unknown '+$key)}
$r=Convert-FixLogEvent ($rotation+' activeClip=PRIVATE completedCycles=PRIVATE alternateAvailable=PRIVATE alternateFailed=PRIVATE') 'output/wallpaper-readiness.log';Check ($r.activeClip -ceq 'unknown' -and $r.completedCycles -eq -1 -and $r.alternateAvailable -eq -1 -and $r.alternateFailed -eq -1) 'rotation duplicate invalid unknown'
$g=Convert-FixLogEvent ($complete+' success=PRIVATE pulseFrames=PRIVATE wallpaperCommitConfirmed=PRIVATE replayExisting=PRIVATE') 'output/handoff-latest.log';Check ($g.success -eq -1 -and $g.pulseFrames -eq -1 -and $g.wallpaperCommitConfirmed -eq -1 -and $g.replayExisting -eq -1) 'handoff duplicate invalid unknown'
$g=Convert-FixLogEvent ($gates+' foreground=PRIVATE mediaCode=PRIVATE refreshCode=PRIVATE') 'output/handoff-latest.log';Check ($g.foreground -ceq 'unknown' -and $g.mediaCode -eq -1 -and $g.refreshCode -eq -1) 'gate duplicate invalid unknown'
$g=Convert-FixLogEvent ($stamp+'Living finale committed; nativeStable=PRIVATE speed=2') 'output/handoff-latest.log';Check ($g.nativeStable -eq -1 -and $g.wallpaperReady -eq -1 -and $g.speed -eq -1) 'missing handoff fields remain unknown'
Check ($null -eq (Convert-FixLogEvent ($quality+' phase=PRIVATE') 'output/wallpaper-readiness.log')) 'ambiguous phase rejected'
Check ($null -eq (Convert-FixLogEvent ($quality+' elapsedMs=PRIVATE') 'output/wallpaper-readiness.log')) 'ambiguous clock rejected'
Check ($null -eq (Convert-FixLogEvent ($stamp+'arbitrary PRIVATE phase=40 elapsedMs=1 code=0 videoTotalFrames=1') 'output/wallpaper-readiness.log')) 'unrelated prefix rejected'
Check ($null -eq (Convert-FixLogEvent ($stamp+'PRIVATE Handoff complete; success=True') 'output/handoff-latest.log')) 'unrelated handoff prefix rejected'
Check ($null -eq (Convert-FixLogEvent (($stamp+'phase=40 elapsedMs=1 code=0 ')+('A'*4096)) 'output/wallpaper-readiness.log')) 'oversized lines rejected'
$script:passed++;Write-Output 'PASS malformed, duplicate, unknown and bounded fields do not become false success'

$numeric=New-SmallManifest 'extended report Chinese 空格'
Save-Text (Join-Path $numeric.root 'output/wallpaper-readiness.log') (@($rotation,$snapshot,$runtime,$quality,$invalidRuntime,($requestFailure+' privatePath=C:\Users\PRIVATE API_KEY=PRIVATE')) -join "`n")
Save-Text (Join-Path $numeric.root 'output/handoff-latest.log') (@($gates,$finale,$complete,$bridge,$resource,$badResource,$nativeWait,$compactWait,$compactPhaseZero,$badNative,($stamp+'Living preparation failed: PRIVATE arbitrary API_KEY=PRIVATE'),($stamp+'arbitrary API_KEY=PRIVATE account@example.invalid C:\Users\PRIVATE https://private.example')) -join "`n")
foreach($name in @('Collect-DiagnosticReport.ps1','DiagnosticReport.Common.ps1')){Copy-Item -LiteralPath (Join-Path $ToolsRoot $name) -Destination $numeric.root}
$output=@(& $powerShell51 -NoProfile -ExecutionPolicy Bypass -File (Join-Path $numeric.root 'Collect-DiagnosticReport.ps1') 2>&1);Check ($LASTEXITCODE -eq 0) 'expanded noarg wrapper'
$extendedZip=@(Get-ChildItem -LiteralPath (Join-Path $numeric.root 'CodexDiagnosticReports') -Filter '*.zip')[0]
$zip=[IO.Compression.ZipFile]::OpenRead($extendedZip.FullName)
try{$reader=[IO.StreamReader]::new($zip.Entries[0].Open());try{$safeText=$reader.ReadToEnd()}finally{$reader.Dispose()}}finally{$zip.Dispose()}
$extended=$safeText|ConvertFrom-Json;$hostEvents=@($extended.logs|Where-Object {$_.log -ceq 'output/wallpaper-readiness.log'})[0].records;$handoffEvents=@($extended.logs|Where-Object {$_.log -ceq 'output/handoff-latest.log'})[0].records
Check ($hostEvents.Count -eq 6 -and $handoffEvents.Count -eq 11 -and $hostEvents[5].operationKind -eq 2 -and $hostEvents[5].requestElapsedMs -eq 750) 'expanded source records survive ZIP pipeline'
Check ($safeText -notmatch '\bPRIVATE\b|API_KEY|account@|private\.example|"pid"|"trace"|"hostPid"|C:\\Users') 'new reports do not leak raw diagnostic text or identifiers'
$bridgeEvents=@($handoffEvents|Where-Object category -ceq 'bridge-diagnostic')
Check ($bridgeEvents.Count -eq 1 -and $bridgeEvents[0].exceptionType -ceq 'None' -and $bridgeEvents[0].hresult -eq 0 -and $bridgeEvents[0].nativeErrorCode -eq -1) 'fixed exception identifiers and numeric codes only; absent native error stays unknown'
$script:passed++;Write-Output 'PASS extended noarg PS5.1 ZIP end-to-end privacy'

$f=New-SmallManifest 'missing-declared-file';$r=Get-PortableDiagnosticReport $f.root;Check ($r.declaredFileCount -eq 1 -and $r.verifiedFileCountAtPreparation -eq -1) 'missing component still permits honest diagnostic report';$script:passed++;Write-Output 'PASS declared file count does not assert file integrity'
$f=New-SmallManifest 'bad-json';Save-Text (Join-Path $f.root 'release-manifest.json') '{invalid';Reject 'malformed manifest JSON' {Get-PortableDiagnosticReport $f.root} 'MANIFEST-JSON'
$f=New-SmallManifest 'oversize';Save-Text (Join-Path $f.root 'release-manifest.json') ('A'*262145);Reject 'manifest bounded before hash' {Get-PortableDiagnosticReport $f.root} 'MANIFEST-SIZE'
$f=New-SmallManifest 'wrong-product';$f.manifest.product='Unrelated';Save-Json (Join-Path $f.root 'release-manifest.json') $f.manifest;Reject 'manifest product rejected' {Get-PortableDiagnosticReport $f.root} 'RELEASE-SCHEMA'
$f=New-SmallManifest 'wrong-schema';$f.manifest.schemaVersion='1';Save-Json (Join-Path $f.root 'release-manifest.json') $f.manifest;Reject 'manifest schema type rejected' {Get-PortableDiagnosticReport $f.root} 'RELEASE-SCHEMA'
$f=New-SmallManifest 'missing-schema';Save-Text (Join-Path $f.root 'release-manifest.json') '{}';Reject 'manifest required fields' {Get-PortableDiagnosticReport $f.root} 'RELEASE-SCHEMA'
$f=New-SmallManifest 'empty-files';$f.manifest.files=@();Save-Json (Join-Path $f.root 'release-manifest.json') $f.manifest;Reject 'empty manifest file list' {Get-PortableDiagnosticReport $f.root} 'FILE-COUNT'
$f=New-SmallManifest 'oversized-file-list';$f.manifest.files=@($f.manifest.files[0])*301;Save-Json (Join-Path $f.root 'release-manifest.json') $f.manifest;Reject 'manifest count bound' {Get-PortableDiagnosticReport $f.root} 'FILE-COUNT'
$f=New-SmallManifest 'duplicate';$f.manifest.files=@($f.manifest.files[0],$f.manifest.files[0]);Save-Json (Join-Path $f.root 'release-manifest.json') $f.manifest;Reject 'duplicate manifest paths' {Get-PortableDiagnosticReport $f.root} 'DUPLICATE-MEMBER'
$f=New-SmallManifest 'bad-hash';$f.manifest.files[0].sha256='abc';Save-Json (Join-Path $f.root 'release-manifest.json') $f.manifest;Reject 'record hash syntax' {Get-PortableDiagnosticReport $f.root} 'FILE-RECORD'
$f=New-SmallManifest 'fractional-bytes';$f.manifest.files[0].bytes=1.5;Save-Json (Join-Path $f.root 'release-manifest.json') $f.manifest;Reject 'record integer byte count' {Get-PortableDiagnosticReport $f.root} 'FILE-RECORD'
$f=New-SmallManifest 'traversal';$f.manifest.files[0].path='../outside.txt';Save-Json (Join-Path $f.root 'release-manifest.json') $f.manifest;Reject 'manifest traversal' {Get-PortableDiagnosticReport $f.root} 'MEMBER-PATH'
$f=New-SmallManifest 'self-manifest';$f.manifest.files[0].path='release-manifest.json';Save-Json (Join-Path $f.root 'release-manifest.json') $f.manifest;Reject 'self-manifest record forbidden' {Get-PortableDiagnosticReport $f.root} 'SELF-MANIFEST'
Reject 'UNC path rejected without network access' {Get-PortableDiagnosticReport '\\example\share'} 'LOCAL-ABSOLUTE-PATH-REQUIRED'
$outside=Join-Path $WorkRoot 'outside';[void][IO.Directory]::CreateDirectory($outside)
$f=New-SmallManifest 'linked-member';[void](New-Item -ItemType Junction -Path (Join-Path $f.root 'linked') -Target $outside);$f.manifest.files[0].path='linked/file.txt';Save-Json (Join-Path $f.root 'release-manifest.json') $f.manifest;Reject 'manifest reparse ancestor' {Get-PortableDiagnosticReport $f.root} 'REPARSE-PATH'
$f=New-SmallManifest 'linked-output';[void](New-Item -ItemType Junction -Path (Join-Path $f.root 'CodexDiagnosticReports') -Target $outside);Reject 'report output reparse' {Write-PortableDiagnosticReport $f.root} 'REPARSE-PATH';Check (@(Get-ChildItem -LiteralPath $outside -Force).Count -eq 0) 'outside untouched'
$f=New-SmallManifest 'linked-log';[void](New-Item -ItemType Junction -Path (Join-Path $f.root 'output') -Target $outside);$r=Get-PortableDiagnosticReport $f.root;$unavailable=@($r.logs|Where-Object {$_.log -ceq 'output/wallpaper-readiness.log'})[0];Check ($unavailable.status -ceq 'unavailable' -and $unavailable.records.Count -eq 0) 'linked logs never read';$script:passed++;Write-Output 'PASS unsafe log path yields no exported data'
$linkedRoot=Join-Path $WorkRoot 'linked-root';[void](New-Item -ItemType Junction -Path $linkedRoot -Target $root);$output=@(& $powerShell51 -NoProfile -ExecutionPolicy Bypass -File (Join-Path $linkedRoot 'Collect-DiagnosticReport.ps1') 2>&1);Check ($LASTEXITCODE -eq 1 -and ($output -join ' ') -match 'FCP-REPARSE-PATH') 'wrapper helper preflight';$script:passed++;Write-Output 'PASS wrapper rejects reparse helper ancestry'
$missing=Join-Path $WorkRoot 'missing-common';[void][IO.Directory]::CreateDirectory($missing);Copy-Item -LiteralPath (Join-Path $ToolsRoot 'Collect-DiagnosticReport.ps1') -Destination $missing;$output=@(& $powerShell51 -NoProfile -ExecutionPolicy Bypass -File (Join-Path $missing 'Collect-DiagnosticReport.ps1') 2>&1);Check ($LASTEXITCODE -eq 1 -and ($output -join ' ') -match 'FCP-IO-OR-FORMAT' -and ($output -join ' ') -notmatch 'missing-common|CommandNotFound') 'missing helper bounded failure';$script:passed++;Write-Output 'PASS missing helper fallback'

$embedded=Join-Path $WorkRoot 'embedded-source';$packaging=Join-Path $embedded 'packaging';$tests=Join-Path $embedded 'tests';[void][IO.Directory]::CreateDirectory($packaging);[void][IO.Directory]::CreateDirectory($tests)
foreach($name in @('Collect-DiagnosticReport.ps1','DiagnosticReport.Common.ps1')){Copy-Item -LiteralPath (Join-Path $ToolsRoot $name) -Destination $packaging}
$embeddedTest=Join-Path $tests 'Test-PortableReport.ps1';Copy-Item -LiteralPath $PSCommandPath -Destination $embeddedTest
$output=@(& $powerShell51 -NoProfile -ExecutionPolicy Bypass -File $embeddedTest -ToolsProbeOnly 2>&1);Check ($LASTEXITCODE -eq 0 -and ($output -join ' ') -match 'PASS portable tools discovery') 'source packaging fallback'
$output=@(& $powerShell51 -NoProfile -ExecutionPolicy Bypass -File $embeddedTest -ToolsRoot $ToolsRoot -ToolsProbeOnly 2>&1);Check ($LASTEXITCODE -eq 0) 'explicit ToolsRoot works'
$script:passed++;Write-Output 'PASS tests resolve standalone tools and source/packaging fallback'
$testBytes=[IO.File]::ReadAllBytes($PSCommandPath);Check ($testBytes[0] -eq 239 -and $testBytes[1] -eq 187 -and $testBytes[2] -eq 191) 'test UTF-8 BOM';$script:passed++;Write-Output 'PASS all PS1 UTF-8 BOM'
Write-Output ('ALL PASS: '+$script:passed+' checks; Windows PowerShell '+$PSVersionTable.PSVersion.ToString()+'; elapsedMs='+$clock.ElapsedMilliseconds+'; collectorRuntimeMs='+$runMs+'. Synthetic data only; no application launched.')
