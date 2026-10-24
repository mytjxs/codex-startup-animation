[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$WorkRoot,[string]$FeedbackReport)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$tools=Join-Path (Split-Path -Parent $PSScriptRoot) 'packaging'
. (Join-Path $tools 'DiagnosticReport.Common.ps1')
. (Join-Path $tools 'DiagnosticAnalysis.ps1')
if($PSVersionTable.PSVersion.Major -ne 5){throw 'Requires Windows PowerShell 5.1'}
[void][IO.Directory]::CreateDirectory($WorkRoot)
$script:checks=0
function Check([bool]$Value,[string]$Name){if(-not $Value){throw ('FAIL '+$Name)};$script:checks++}
function HostEvent([int]$Second,[int]$Phase,[int]$Code=0){return [pscustomobject]@{utc=('2026-10-24T12:00:{0:D2}Z' -f $Second);category='host-phase';phase=$Phase;code=$Code;elapsedMs=$Second*1000}}
function Entry([int]$Second,[string]$Stage='launcher-entry'){return [pscustomobject]@{utc=('2026-10-24T12:00:{0:D2}Z' -f $Second);category='launcher-stage';stage=$Stage;elapsedMs=0}}
function Report($HostEvents,$LauncherEvents){return [pscustomobject]@{logs=@([pscustomobject]@{log='output/wallpaper-readiness.log';records=@($HostEvents)},[pscustomobject]@{log='output/handoff-latest.log';records=@($LauncherEvents)})}}
$stamp='2026-10-24T12:00:01.0000000Z '
$loss=Convert-FixLogEvent ($stamp+'phase=46 elapsedMs=12 code=0 sourcePhase=12 lossKind=target-detached transportFaultCode=0 processExited=0 trace=0123456789abcdef0123456789abcdef pid=4321 secret=PRIVATE') 'output/wallpaper-readiness.log'
Check ((Get-CodexAnalysisValue $loss 'lossKind') -ceq 'target-detached') 'loss event keeps fixed reason separately from process exit'
Check ($loss.sourcePhase -eq 12 -and $loss.processExited -eq 0 -and $loss.transportFaultCode -eq 0) 'loss numeric fields retained'
Check (($loss|ConvertTo-Json -Compress) -notmatch 'PRIVATE|0123456789|4321|trace|pid') 'loss event never exports private extra fields'
foreach($kind in @('transport-fault','target-detached','target-crashed','inspector-detached','unknown')){
    $item=Convert-FixLogEvent ($stamp+'phase=46 elapsedMs=1 code=0 sourcePhase=12 lossKind='+$kind+' transportFaultCode=35 processExited=1') 'output/wallpaper-readiness.log'
    Check ($item.lossKind -ceq $kind) ('loss enum '+$kind)
}
foreach($state in @(-1,0,1)){
    $item=Convert-FixLogEvent ($stamp+'phase=46 elapsedMs=1 code=0 sourcePhase=12 lossKind=transport-fault transportFaultCode=-1 processExited='+$state) 'output/wallpaper-readiness.log'
    Check ($item.processExited -eq $state -and $item.transportFaultCode -eq -1) ('loss unknown/alive/exited state '+$state)
}
$exit=Convert-FixLogEvent ($stamp+'phase=47 elapsedMs=20 code=0 processExited=1 exitCodeKnown=1 processExitCode=4294967295') 'output/wallpaper-readiness.log'
Check ($exit.processExited -eq 1 -and $exit.exitCodeKnown -eq 1 -and $exit.processExitCode -eq 4294967295L) 'unsigned Windows process exit code remains bounded number'
$bad=Convert-FixLogEvent ($stamp+'phase=46 elapsedMs=12 code=0 sourcePhase=64 lossKind=PRIVATE transportFaultCode=100 processExited=2') 'output/wallpaper-readiness.log'
Check ($bad.sourcePhase -eq -1 -and $bad.lossKind -ceq 'unknown' -and $bad.transportFaultCode -eq -1 -and $bad.processExited -eq -1) 'invalid loss evidence stays unknown'
$bad=Convert-FixLogEvent ($stamp+'phase=46 elapsedMs=12 code=0 sourcePhase=12 lossKind=target-detached lossKind=PRIVATE transportFaultCode=0 transportFaultCode=PRIVATE processExited=1 processExited=PRIVATE') 'output/wallpaper-readiness.log'
Check ($bad.lossKind -ceq 'unknown' -and $bad.transportFaultCode -eq -1 -and $bad.processExited -eq -1) 'duplicate loss fields cannot prove success or failure'
$bad=Convert-FixLogEvent ($stamp+'phase=47 elapsedMs=12 code=0 processExited=1 exitCodeKnown=1 processExitCode=4294967296') 'output/wallpaper-readiness.log'
Check ($bad.processExitCode -eq -1) 'out of range process exit rejected'
Check ($null -eq (Convert-FixLogEvent ($stamp+'phase=46 elapsedMs=12 code=0 lossKind=target-detached') 'output/handoff-latest.log')) 'loss source is host-only'
$visibility=Convert-FixLogEvent ($stamp+'phase=49 elapsedMs=20 code=6 releaseReason=2 eligibleHidden=1 visible=0 markerOwned=1 account=PRIVATE') 'output/wallpaper-readiness.log'
Check ($visibility.releaseReason -eq 2 -and $visibility.eligibleHidden -eq 1 -and $visibility.visible -eq 0 -and $visibility.markerOwned -eq 1) 'initial visibility outcome retains distinct main candidate counts'
$visibility=Convert-FixLogEvent ($stamp+'phase=49 elapsedMs=20 code=3 releaseReason=7 eligibleHidden=130 visible=PRIVATE markerOwned=2') 'output/wallpaper-readiness.log'
Check ($visibility.releaseReason -eq -1 -and $visibility.eligibleHidden -eq -1 -and $visibility.visible -eq -1 -and $visibility.markerOwned -eq -1) 'visibility outliers do not become candidate evidence'
foreach($value in @('0','1','-1','2','PRIVATE','1 observationComplete=0','')){
    $line=$stamp+'phase=49 elapsedMs=20 code=7 releaseReason=2 eligibleHidden=1 visible=0 markerOwned=1'
    if($value -ne ''){$line+=' observationComplete='+$value}
    $observation=Convert-FixLogEvent $line 'output/wallpaper-readiness.log'
    $expected=if($value -eq '0'){0}elseif($value -eq '1'){1}else{-1}
    Check ((Get-CodexAnalysisValue $observation 'observationComplete') -eq $expected) ('visibility observation complete whitelist '+$value)
}
$runs=@{};$traceA='0123456789abcdef0123456789abcdef';$traceB='fedcba9876543210fedcba9876543210'
Check ((Get-FixHostRunOrdinal ('trace='+$traceA) $runs) -eq 1 -and (Get-FixHostRunOrdinal ('trace='+$traceB) $runs) -eq 2 -and (Get-FixHostRunOrdinal ('trace='+$traceA) $runs) -eq 1) 'report-local ordinal stable only within collection'
Check ((Get-FixHostRunOrdinal ('trace='+$traceA+' trace=PRIVATE') $runs) -eq -1 -and (Get-FixHostRunOrdinal 'trace=PRIVATE' $runs) -eq -1 -and $runs.Count -eq 2) 'malformed and duplicate traces never allocate identities'
for($i=2;$i -lt 32;$i++){[void](Get-FixHostRunOrdinal ('trace='+$i.ToString('x32')) $runs)}
Check ($runs.Count -eq 32 -and (Get-FixHostRunOrdinal 'trace=ffffffffffffffffffffffffffffffff' $runs) -eq -1) 'ordinal map fixed at 32 hosts'
$runs[$traceA]=999;Check ((Get-FixHostRunOrdinal ('trace='+$traceA) $runs) -eq -1) 'corrupted ordinal map cannot emit unbounded integer'
$fixture=Join-Path $WorkRoot 'logs';[void][IO.Directory]::CreateDirectory((Join-Path $fixture 'output'))
[IO.File]::WriteAllText((Join-Path $fixture 'output\wallpaper-readiness.log.1'),($stamp+'phase=20 elapsedMs=0 code=0 trace='+$traceA+' pid=4321'),[Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllText((Join-Path $fixture 'output\wallpaper-readiness.log'),($stamp+'phase=46 elapsedMs=1 code=0 sourcePhase=12 lossKind=transport-fault transportFaultCode=35 processExited=0 trace='+$traceA+"`n"+$stamp+'phase=20 elapsedMs=0 code=0 trace='+$traceB),[Text.UTF8Encoding]::new($false))
$map=@{};$one=Read-FixLogTail $fixture 'output/wallpaper-readiness.log.1' $map;$two=Read-FixLogTail $fixture 'output/wallpaper-readiness.log' $map
Check ($one.records[0].hostRunOrdinal -eq 1 -and $two.records[0].hostRunOrdinal -eq 1 -and $two.records[1].hostRunOrdinal -eq 2) 'rotation preserves report-local host continuity'
Check ((@($one,$two)|ConvertTo-Json -Depth 8 -Compress) -notmatch ($traceA+'|'+$traceB+'|4321|PRIVATE')) 'serialized rotated evidence never exposes identities'

$events=@((HostEvent 1 20),(HostEvent 7 1 137),(HostEvent 7 15 137),(HostEvent 20 32),(HostEvent 22 20))
$events+=HostEvent 30 11
$playing=HostEvent 35 39
foreach($pair in @(@('generationMatches',1),@('protocolVersion',2),@('health','fresh'),@('attached',1),@('disposed',0),@('mediaReady',1),@('playing',1),@('wallpaperApplied',1),@('firstFrameConfirmed',1),@('layoutSupported',1),@('revealed',1),@('documentHidden',0),@('stageVisible',1),@('commitAccepted',1),@('actualPhase','visible'),@('presentedFrames',20))){$playing|Add-Member -NotePropertyName $pair[0] -NotePropertyValue $pair[1]}
$events+=$playing
$loss.utc='2026-10-24T12:00:40Z';$events+=$loss
$events+=HostEvent 40 12 172
$exit.utc='2026-10-24T12:00:45Z';$exit.processExitCode=0;$events+=$exit;$events+=HostEvent 45 32
$handoff=[pscustomobject]@{utc='2026-10-24T12:00:34Z';category='handoff-complete';success=1;wallpaperCommitConfirmed=1;replayExisting=0}
$launcher=@((Entry 0),[pscustomobject]@{utc='2026-10-24T12:00:08Z';category='launcher-cleanup-complete'},(Entry 15),(Entry 16 'replay-target-unavailable-no-activation'),(Entry 21),$handoff)
$report=Report $events $launcher
$history=@(Get-CodexStartupHistoryAnalysis $report) -join "`n"
Check ($history.Contains('启动尝试 1') -and $history.Contains('启动尝试 2') -and $history.Contains('启动尝试 3')) 'all three attempts remain visible'
Check ($history.Contains('137') -and $history.Contains('目标页面发现')) 'first target discovery failure survives later success'
Check ($history.Contains('重播未找到合格窗口')) 'failed hot retry remains distinct from previous host exit'
Check ($history.Contains('完成交接') -and $history.Contains('确认过壁纸显示')) 'latest successful handoff and playback history retained'
Check ($history.Contains('目标会话分离') -and $history.Contains('5000 毫秒') -and $history.Contains('不能据此判断用户主动退出')) 'loss and later process exit explain timing without inferring intent'
Check ($history.Contains('退出码 0') -and $history.Contains('采样时进程：仍存活')) 'exit code shown separately from loss-time alive observation'
Check ($history -notmatch 'PRIVATE|0123456789|4321') 'history never echoes raw values'
$events[-2].processExited=0
$withoutExit=@(Get-CodexStartupHistoryAnalysis (Report $events $launcher)) -join "`n"
Check (-not $withoutExit.Contains('退出记录相隔 5000')) 'invalid exit event cannot prove exit interval'
$events[-2].processExited=1
$report.logs[0].records=@($events+$events)
$duplicate=@(Get-CodexStartupHistoryAnalysis $report) -join "`n"
Check ($duplicate -eq $history) 'identical duplicate records do not change timeline'
$report.logs[0].records=@($events)
$crash=HostEvent 39 46
foreach($pair in @(@('sourcePhase',12),@('lossKind','target-crashed'),@('transportFaultCode',0),@('processExited',0))){$crash|Add-Member -NotePropertyName $pair[0] -NotePropertyValue $pair[1]}
$transport=HostEvent 41 46
foreach($pair in @(@('sourcePhase',12),@('lossKind','transport-fault'),@('transportFaultCode',35),@('processExited',-1))){$transport|Add-Member -NotePropertyName $pair[0] -NotePropertyValue $pair[1]}
$report.logs[0].records=@($events+$crash+$transport)
$text=@(Get-CodexStartupHistoryAnalysis $report) -join "`n"
Check ($text.Contains('渲染目标崩溃通知') -and $text.Contains('通信管道故障') -and $text.Contains('目标会话分离') -and $text.Contains('采样时进程：未知或冲突')) 'later transport loss cannot bury earlier explicit target-crashed notification'
$events[-2].exitCodeKnown=0;$report.logs[0].records=@($events)
Check ((@(Get-CodexStartupHistoryAnalysis $report) -join "`n").Contains('退出码 未知或冲突')) 'unknown exit code is not replaced by numeric zero'
$events[-2].exitCodeKnown=1
$newStart=HostEvent 22 20;$newStart|Add-Member -NotePropertyName hostRunOrdinal -NotePropertyValue 9
$report.logs[0].records=@($events+$newStart)
Check ((@(Get-CodexStartupHistoryAnalysis $report) -join "`n").Contains('出现多个新宿主')) 'same-time distinct host starts refuse unique attribution'
$report.logs[0].records=@($events)
$conflict=HostEvent 35 39;$conflict|Add-Member -NotePropertyName disposed -NotePropertyValue 1
$report.logs[0].records=@($events+$conflict)
$ambiguous=@(Get-CodexStartupHistoryAnalysis $report) -join "`n"
Check (-not $ambiguous.Contains('确认过壁纸显示')) 'same-time conflicting playback does not create historical proof'
$report.logs[0].records=@($events)
$duplicateStart=HostEvent 23 20;$duplicateStart|Add-Member -NotePropertyName hostRunOrdinal -NotePropertyValue 2
$report.logs[0].records=@($events+$duplicateStart)
Check ((@(Get-CodexStartupHistoryAnalysis $report) -join "`n").Contains('出现多个新宿主')) 'different start times in one attempt stay ambiguous'
$report.logs[0].records=@($events)
$v1=HostEvent 40 49;$v2=HostEvent 40 49;$v1.code=6;$v2.code=3;$v1|Add-Member -NotePropertyName hostRunOrdinal -NotePropertyValue 2;$v2|Add-Member -NotePropertyName hostRunOrdinal -NotePropertyValue 2
$report.logs[0].records=@($events+$v1+$v2)
Check (-not ((@(Get-CodexStartupHistoryAnalysis $report) -join "`n").Contains('曾排队释放'))) 'conflicting same-time visibility outcome cannot select show success'
$v1.code=7;$v1|Add-Member -NotePropertyName observationComplete -NotePropertyValue 0
$report.logs[0].records=@($events+$v1)
$text=@(Get-CodexStartupHistoryAnalysis $report) -join "`n"
Check ($text.Contains('窗口枚举未完整完成') -and -not $text.Contains('候选失效')) 'incomplete visibility observation cannot certify invalid candidate'
$v1.observationComplete=1;$report.logs[0].records=@($events+$v1)
Check ((@(Get-CodexStartupHistoryAnalysis $report) -join "`n").Contains('隐藏窗口候选的所有权或状态校验未通过')) 'complete observation does not invent prior marker ownership'
$v2.code=7;$v2|Add-Member -NotePropertyName observationComplete -NotePropertyValue 0
$report.logs[0].records=@($events+$v1+$v2)
$text=@(Get-CodexStartupHistoryAnalysis $report) -join "`n"
Check ($text.Contains('无法区分') -and -not $text.Contains('原先保留的隐藏窗口候选失效')) 'same-time completeness conflict stays unknown'
$report.logs[0].records=@($events)
$contradictory=HostEvent 45 47;$contradictory|Add-Member -NotePropertyName processExited -NotePropertyValue 0
$report.logs[0].records=@($events+$contradictory)
Check (-not ((@(Get-CodexStartupHistoryAnalysis $report) -join "`n").Contains('退出记录相隔 5000'))) 'same-time alive/dead conflict cannot certify process exit'
$report.logs[0].records=@($events+(HostEvent 7 15 168))
$text=@(Get-CodexStartupHistoryAnalysis $report) -join "`n"
Check ($text.Contains('首次故障同一时刻存在不同错误码') -and -not $text.Contains('目标页面发现等待')) 'same-time different error codes stay ambiguous'
$report.logs[0].records=@($events)
foreach($item in $report.logs[0].records){$item|Add-Member -NotePropertyName hostRunOrdinal -NotePropertyValue $(if($item.utc -lt '2026-10-24T12:00:22Z'){1}else{2}) -Force}
$foreign=HostEvent 33 15 168;$foreign|Add-Member -NotePropertyName hostRunOrdinal -NotePropertyValue 1
$report.logs[0].records=@($events+$foreign)
$text=@(Get-CodexStartupHistoryAnalysis $report) -join "`n"
$lastLine=@($text -split "`n"|Where-Object {$_ -like '启动尝试 3*'})[0]
Check ($lastLine -notmatch '168' -and $lastLine.Contains('确认过壁纸显示')) 'older host cannot contaminate later attempt despite overlapping time'
$report.logs[0].records=@($events)
$many=@();for($i=0;$i -lt 10;$i++){$many+=Entry ($i*5)}
$bounded=@(Get-CodexStartupHistoryAnalysis (Report @() $many)) -join "`n"
Check (([regex]::Matches($bounded,'启动尝试 \d+').Count -eq 8) -and $bounded.Contains('最近 8 次')) 'historical text bounded at eight attempts'
$large=Report @() @($launcher[0]);$large.logs[0].records=@(1..513|ForEach-Object {HostEvent 1 20})
Check (@(Get-CodexStartupHistoryAnalysis $large).Count -eq 0) 'overlarge event stream refuses analysis'
$report.logs+= [pscustomobject]@{log='output/handoff-latest.log';records=@($launcher)}
Check (@(Get-CodexStartupHistoryAnalysis $report).Count -eq 0) 'duplicate authoritative launcher streams refuse attribution'
$report=Report $events $launcher
$text=Get-CodexDiagnosticAnalysis $report $null
Check ($text.Contains('启动尝试 1') -and $text.Contains('137')) 'full analysis includes prior failures alongside latest state'
if($FeedbackReport){
    $feedback=Get-Content -LiteralPath $FeedbackReport -Raw|ConvertFrom-Json
    $text=Get-CodexDiagnosticAnalysis $feedback $null
    Check ($text.Contains('启动尝试 3') -and $text.Contains('137') -and $text.Contains('确认过壁纸显示') -and $text.Contains('172')) 'submitted report preserves failure, replay refusal and displayed ambient history'
    [IO.File]::WriteAllText((Join-Path $WorkRoot 'feedback-analysis.txt'),$text,[Text.UTF8Encoding]::new($false))
}
[IO.File]::WriteAllText((Join-Path $WorkRoot 'results.json'),(@{passed=$true;checks=$checks}|ConvertTo-Json),[Text.UTF8Encoding]::new($false))
Write-Output ('ALL PASS: startup history diagnostics '+$checks)
