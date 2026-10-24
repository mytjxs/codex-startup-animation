[CmdletBinding()]
param([string]$ToolsRoot,[Parameter(Mandatory=$true)][string]$WorkRoot)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
if(-not $PSBoundParameters.ContainsKey('ToolsRoot')){$ToolsRoot=Join-Path (Split-Path -Parent (Split-Path -Parent $PSCommandPath)) 'packaging'}
. (Join-Path $ToolsRoot 'DiagnosticAnalysis.ps1')
if($PSVersionTable.PSVersion.Major -ne 5){throw 'Tests require Windows PowerShell 5.1'}
[void][IO.Directory]::CreateDirectory($WorkRoot)
$script:checks=0
function Check([bool]$Value,[string]$Name){if(-not $Value){throw ('TEST FAILED: '+$Name)};$script:checks++;Write-Host ('PASS '+$Name)}
function Report($Events=@(),$Bridge=@()){
    return [pscustomobject]@{manifestState='valid';mediaPresence=@(@('startup','intro','idle','finale','ambient','particles')|ForEach-Object {[pscustomobject]@{role=$_;state='present';sizeMatches=1;hashVerified=$false;decodingVerified=$false}});compatibility=[pscustomobject]@{dotNetFrameworkRelease=528040;registrationState='ok';registeredPackageCount=1;packages=@([pscustomobject]@{runtimeEvidence='electron-evidence'})};logs=@([pscustomobject]@{log='output/wallpaper-readiness.log';records=@($Events)},[pscustomobject]@{log='output/handoff-latest.log';records=@($Bridge)})}
}
function Event([string]$Time,[string]$State){
    if($State -eq 'fail'){return [pscustomobject]@{utc=$Time;category='host-phase';phase=15;code=168}}
    return [pscustomobject]@{utc=$Time;category='host-phase';phase=39;code=0;layoutSupported=1;layoutReason='ready';playing=1;wallpaperApplied=1;firstFrameConfirmed=1;rendererCode='playing';videoErrorCode=0;generationMatches=1;protocolVersion=2;health='fresh';attached=1;disposed=0;mediaReady=1;revealed=1;documentHidden=0;stageVisible=1;commitAccepted=1}
}
$check=[pscustomobject]@{state='same-manifest';passed=$true;fileCount=125;failure='none'}
$old=Event '2026-10-22T10:00:00Z' 'fail';$new=Event '2026-10-22T10:00:01Z' 'success'
$text=Get-CodexDiagnosticAnalysis (Report @($new,$old)) $check
Check ($text.Contains('当时确认了首帧') -and -not $text.Contains('视频或首帧准备没有完成')) 'timestamp order supersedes old failure'
Check ($text.Contains('125 项通过') -and $text.Contains('不证明解码成功') -and $text.Contains('不是实时运行探测')) 'file pass and historical state caveats'
Check ($text.Contains('六类视频') -and $text.Contains('Electron 相关文件线索')) 'single package array survives projection'
$text=Get-CodexDiagnosticAnalysis (Report @($new,$new,$old)) $check
Check ($text.Contains('当时确认了首帧') -and -not $text.Contains('相互不同的状态')) 'identical duplicated log records do not invent conflict'
$sameFailure=Event '2026-10-22T10:00:01Z' 'fail'
$text=Get-CodexDiagnosticAnalysis (Report @($new,$sameFailure)) $check
Check ($text.Contains('相互不同的状态') -and -not $text.Contains('当时确认了首帧')) 'same timestamp conflicting records remain ambiguous'
$entry=[pscustomobject]@{utc='2026-10-22T10:00:02Z';category='launcher-stage';stage='launcher-entry'}
$text=Get-CodexDiagnosticAnalysis (Report @($old,$new) @($entry)) $null
Check ($text.Contains('没有可解释的本次运行状态记录')) 'new launch boundary discards prior run evidence'
$replayStages=@()
foreach($pair in @(@('2026-10-22T12:28:21.8820451Z','2026-10-22T12:28:22.0485797Z'),@('2026-10-22T12:28:37.4311388Z','2026-10-22T12:28:37.5867939Z'),@('2026-10-22T12:28:48.1663534Z','2026-10-22T12:28:48.3585317Z'))){
    $replayStages+=[pscustomobject]@{utc=$pair[0];category='launcher-stage';stage='launcher-entry'}
    $replayStages+=[pscustomobject]@{utc=$pair[1];category='launcher-stage';stage='replay-target-unavailable-no-activation';elapsedMs=194}
}
$previousWallpaper=Event '2026-10-22T12:28:44.0682041Z' 'success'
$text=Get-CodexDiagnosticAnalysis (Report @($previousWallpaper) $replayStages) $null
Check ($text.Contains('重复启动分支未找到可验证的目标窗口') -and -not $text.Contains('没有可解释的本次运行状态记录') -and -not $text.Contains('当时确认了首帧')) 'three replay exits retain latest explicit request failure instead of old wallpaper proof'
$oldHostContinues=Event '2026-10-22T12:28:49Z' 'success'
$text=Get-CodexDiagnosticAnalysis (Report @($oldHostContinues) $replayStages) $null
Check ($text.Contains('重复启动分支未找到可验证的目标窗口') -and -not $text.Contains('当时确认了首帧')) 'existing host playback cannot erase failed replay request'
$nextEntry=[pscustomobject]@{utc='2026-10-22T12:29:00Z';category='launcher-stage';stage='launcher-entry'}
$text=Get-CodexDiagnosticAnalysis (Report @($previousWallpaper) ($replayStages+@($nextEntry))) $null
Check ($text.Contains('没有可解释的本次运行状态记录') -and -not $text.Contains('重复启动分支未找到可验证的目标窗口')) 'next launcher entry retires old replay failure'
$maliciousStage=[pscustomobject]@{utc='2026-10-22T12:29:01Z';category='launcher-stage';stage='replay-target-unavailable-no-activation PRIVATE'}
$text=Get-CodexDiagnosticAnalysis (Report @() @($nextEntry,$maliciousStage)) $null
Check (-not $text.Contains('PRIVATE') -and -not $text.Contains('重复启动分支未找到可验证的目标窗口')) 'replay stage remains an exact allowlisted value'
$foreignStage=Report @($replayStages[-1])
Check (-not (Get-CodexDiagnosticAnalysis $foreignStage $null).Contains('重复启动分支未找到可验证的目标窗口')) 'launcher stage from host log is not authoritative'
$cached=[pscustomobject]@{utc='2026-10-22T10:00:03Z';category='host-phase';phase=10;sampleAgeMs=7000;layoutSupported=1;playing=1;wallpaperApplied=1;firstFrameConfirmed=1}
$text=Get-CodexDiagnosticAnalysis (Report @($cached)) $null
Check ($text.Contains('过期快照') -and -not $text.Contains('当时确认了首帧')) 'late cached snapshot is not fresh proof'
$fresh=Event '2026-10-22T10:00:03Z' 'success';$fresh.phase=10;$fresh|Add-Member -NotePropertyName sampleAgeMs -NotePropertyValue 100;$fresh|Add-Member -NotePropertyName prepared -NotePropertyValue 1
Check ((Get-CodexDiagnosticAnalysis (Report @($fresh)) $null).Contains('媒体已经准备')) 'bounded recent preparation snapshot does not invent visible playback'
foreach($field in @('generationMatches','protocolVersion','attached','mediaReady','revealed','stageVisible','commitAccepted')){$stale=Event '2026-10-22T10:00:04Z' 'success';$stale.$field=0;Check (-not (Get-CodexDiagnosticAnalysis (Report @($stale)) $null).Contains('当时确认了首帧')) ('incomplete evidence cannot confirm playing: '+$field)}
$stale=Event '2026-10-22T10:00:04Z' 'success';$stale.health='stale';Check (-not (Get-CodexDiagnosticAnalysis (Report @($stale)) $null).Contains('当时确认了首帧')) 'stale health cannot confirm playing'
Check (-not (Get-CodexDiagnosticAnalysis (Report @($new,$stale)) $null).Contains('当时确认了首帧')) 'new contradictory snapshot supersedes old valid-playing proof'
$stale=Event '2026-10-22T10:00:04Z' 'success';$stale.disposed=1;Check ((Get-CodexDiagnosticAnalysis (Report @($stale)) $null).Contains('壁纸渲染已结束')) 'disposed contradiction is not playing'
$stale=Event '2026-10-22T10:00:04Z' 'success';$stale.documentHidden=1;Check ((Get-CodexDiagnosticAnalysis (Report @($stale)) $null).Contains('页面不可见')) 'hidden contradiction is not visible playback'
$r=Report;$r.compatibility.dotNetFrameworkRelease=528039;$text=Get-CodexDiagnosticAnalysis $r $null
Check ($text.Contains('低于 4.8') -and $text.Contains('没有可解释的本次运行状态记录')) 'below minimum .NET and absent logs remain distinct'
$r.compatibility.dotNetFrameworkRelease='528040 SECRET';$r.compatibility.packages=@([pscustomobject]@{runtimeEvidence='webview2-evidence';path='C:\Users\PRIVATE\SECRET'})
$text=Get-CodexDiagnosticAnalysis $r $null
Check ($text.Contains('版本记录不可用') -and $text.Contains('不能据此断言客户端采用 Tauri') -and -not $text.Contains('SECRET')) 'strict dependency number and uncertain WebView2 evidence'
$r=Report;$r.mediaPresence[4].state='missing';$r.mediaPresence[5].sizeMatches=0;$text=Get-CodexDiagnosticAnalysis $r $null
Check ($text.Contains('常态壁纸视频：文件缺失') -and $text.Contains('粒子壁纸视频：大小与清单不符')) 'missing and wrong-size media distinguished'
$r=Report;$r.mediaPresence+= $r.mediaPresence[4];$text=Get-CodexDiagnosticAnalysis $r $null
Check ($text.Contains('检查记录不完整或存在重复项') -and -not $text.Contains('六类视频的存在和大小记录匹配')) 'duplicate role cannot prove complete package'
$bridge=[pscustomobject]@{utc='2026-10-22T10:00:02Z';category='bridge-diagnostic';outcome='wallpaper-unavailable-existing-process'}
$text=Get-CodexDiagnosticAnalysis (Report @($old) @($bridge)) $null
Check ($text.Contains('完全退出 Codex') -and $text.Contains('最外层 Launch.exe') -and $text.Contains('不要反复连点')) 'unhosted reuse gets specific bounded next step'
$later=Event '2026-10-22T10:00:03Z' 'success';$text=Get-CodexDiagnosticAnalysis (Report @($later) @($bridge)) $null
Check ($text.Contains('当时确认了首帧') -and -not $text.Contains('完全退出 Codex')) 'old ordinary-reuse no longer drives current recommendation'
$bad=[pscustomobject]@{utc='2026-10-22T10:00:99Z SECRET';category='host-phase';phase=15;code=168;layoutReason='C:\Users\PRIVATE\SECRET'}
$r=Report @($bad);$r.compatibility.packages=@([pscustomobject]@{runtimeEvidence='SECRET'});$text=Get-CodexDiagnosticAnalysis $r ([pscustomobject]@{state='same-manifest';passed='true SECRET';failure='SECRET';fileCount='125 SECRET'})
Check ($text -notmatch 'PRIVATE|SECRET|C:\\Users' -and $text.Contains('暂不能确定框架') -and $text.Contains('没有可确认的完整文件校验记录')) 'malicious values never echoed or coerced'
$r=Report;$r.manifestState='invalid-or-missing';$text=Get-CodexDiagnosticAnalysis $r $null
Check ($text.Contains('发布清单缺失或无效') -and $text.Contains('不等于应用运行失败')) 'broken extraction supports honest partial report'
$r=Report @([pscustomobject]@{utc='2026-10-22T10:00:01Z';category='host-phase';phase=38;code=0;layoutSupported=0;layoutReason='composer-ambiguous'})
Check ((Get-CodexDiagnosticAnalysis $r $null).Contains('输入区域布局存在歧义')) 'specific layout diagnosis uses fixed text'
$r=Report @([pscustomobject]@{utc='2026-10-22T10:00:01Z';category='host-phase';phase=39;code=0;layoutSupported=1;videoErrorCode=3})
Check ((Get-CodexDiagnosticAnalysis $r $null).Contains('视频媒体组件报告错误')) 'media error distinct from missing file'
$text=Get-CodexDiagnosticAnalysis $null $null
Check ($text.Contains('没有可解释') -and $text.Contains('没有执行显卡') -and $text.Contains('不是实时 FPS') -and $text.Length -le 16384) 'null input hardware unknown and bounded no-pressure output'
foreach($failure in @('platform-check','manifest-check','file-integrity','framework-metadata')){Check ((Get-CodexDiagnosticAnalysis (Report) ([pscustomobject]@{state='same-manifest';passed=$false;failure=$failure;fileCount=0})).Length -lt 16384) ('fixed file check failure '+$failure)}
# Synthetic values mirror a complete 30.6609445-second startup; no user data or paths.
function StartupEntry([string]$Time){return [pscustomobject]@{utc=$Time;category='launcher-stage';stage='launcher-entry'}}
function StartupEnd([string]$Time){return [pscustomobject]@{utc=$Time;category='handoff-complete';success=1}}
function Resource([string]$Time,$Private,$Cpu,$Available,$Low=0){return [pscustomobject]@{utc=$Time;category='startup-resource-sample';intervalMs=2000;privateBytes=$Private;cpuPermille=$Cpu;availablePhysicalBytes=$Available;lowMemory=$Low}}
$complete=@((StartupEntry '2026-10-22T12:27:31.8156398Z'),(Resource '2026-10-22T12:27:42.2252487Z' 1137999872 66 4503744512),(Resource '2026-10-22T12:27:48.2360507Z' 1071341568 74 3982004224),(Resource '2026-10-22T12:27:56.2508982Z' 1023725568 64 2321342464),(StartupEnd '2026-10-22T12:28:02.4765843Z'))
$text=Get-CodexDiagnosticAnalysis (Report @() ($complete+$replayStages)) $null
Check ($text.Contains('最近一次有结束记录的启动：30660 毫秒') -and $text.Contains('重复启动分支未找到可验证的目标窗口')) 'completed startup duration remains distinct from newer failed replay request'
Check ($text.Contains('1137999872 字节') -and $text.Contains('2321342464 字节') -and $text.Contains('7.4%') -and $text.Contains('整机 CPU 总容量')) 'bounded startup resource extrema retain process and CPU normalization scope'
Check ($text.Contains('采样中未记录低内存标志') -and $text.Contains('不能单凭这些采样确定 CPU、GPU 或其他程序是瓶颈')) 'sample-only memory and performance caveats avoid causal claims'
$reversed=@($complete);[array]::Reverse($reversed)
Check ((Get-CodexDiagnosticAnalysis (Report @() $reversed) $null).Contains('最近一次有结束记录的启动：30660 毫秒')) 'strict UTC ordering does not depend on record order'
$text=Get-CodexDiagnosticAnalysis (Report @() @($complete[1],$complete[-1])) $null
Check (-not $text.Contains('最近一次有结束记录的启动：')) 'missing startup entry does not invent elapsed time'
$text=Get-CodexDiagnosticAnalysis (Report @() @($complete[0],$complete[1])) $null
Check (-not $text.Contains('最近一次有结束记录的启动：')) 'missing startup completion does not invent elapsed time'
$later=@((StartupEntry '2026-10-22T12:30:00Z'),(Resource '2026-10-22T12:30:01Z' 1048576 10 2097152 1),(StartupEnd '2026-10-22T12:30:02Z'))
$text=Get-CodexDiagnosticAnalysis (Report @() ($complete+$later)) $null
Check ($text.Contains('启动：2000 毫秒') -and $text.Contains('1048576 字节') -and -not $text.Contains('1137999872 字节') -and $text.Contains('采样中记录了低内存标志')) 'latest completed interval replaces earlier metrics without mixing sessions'
$outside=Resource '2026-10-22T12:30:03Z' 281474976710656 999 1 1
$text=Get-CodexDiagnosticAnalysis (Report @() ($complete+@($outside))) $null
Check ($text.Contains('7.4%') -and -not $text.Contains('99.9%') -and -not $text.Contains('281474976710656 字节')) 'resource samples after completion are excluded'
$unknown=Resource '2026-10-22T12:27:42Z' 'SECRET' '74' 1.5 $null
$text=Get-CodexDiagnosticAnalysis (Report @() @($complete[0],$unknown,$complete[-1])) $null
Check ($text -notmatch 'SECRET|7\.4%' -and $text.Contains('私有内存采样值不可用') -and $text.Contains('CPU 采样值不可用') -and $text.Contains('可用物理内存采样值不可用') -and $text.Contains('低内存标志记录不完整')) 'strict numeric fields do not coerce strings floats null or arbitrary text'
$invalid=StartupEntry '2026-10-22T12:27:31+00:00'
Check (-not (Get-CodexDiagnosticAnalysis (Report @() @($invalid,$complete[-1])) $null).Contains('最近一次有结束记录的启动：')) 'noncanonical UTC boundary cannot prove complete interval'
$r=Report @() $complete;$r.logs+= $r.logs[1]
Check (-not (Get-CodexDiagnosticAnalysis $r $null).Contains('最近一次有结束记录的启动：')) 'duplicate handoff log sources cannot prove resource interval'
$r=Report @() $complete;$r.logs[1].records=@(1..513|ForEach-Object {$complete[0]})
Check (-not (Get-CodexDiagnosticAnalysis $r $null).Contains('最近一次有结束记录的启动：')) 'resource analysis obeys record count cap'
$overlap=StartupEntry '2026-10-22T12:27:40Z'
Check (-not (Get-CodexDiagnosticAnalysis (Report @() ($complete+@($overlap))) $null).Contains('最近一次有结束记录的启动：')) 'overlapping startup entries cannot be paired speculatively'
$foreign=Report $complete
Check (-not (Get-CodexDiagnosticAnalysis $foreign $null).Contains('最近一次有结束记录的启动：')) 'host log resource lookalikes are not authoritative'
$protectedStart=StartupEntry '2026-10-22T13:21:51Z'
$protection=[pscustomobject]@{utc='2026-10-22T13:22:12Z';category='startup-resource-protection';reason='startup-resource-protection'}
$retryStart=StartupEntry '2026-10-22T13:22:25Z'
$attest=[pscustomobject]@{utc='2026-10-22T13:22:25.5Z';category='replay-diagnostic';operation='attest';phase=6;reason=0}
$hidden=[pscustomobject]@{utc='2026-10-22T13:22:25.6Z';category='replay-native-candidates';phase=4;verifiedWindows=8;visible=0;hidden=8;cloaked=0;eligible=0}
$retryFailed=[pscustomobject]@{utc='2026-10-22T13:22:25.7Z';category='launcher-stage';stage='replay-target-unavailable-no-activation'}
$cancelled=[pscustomobject]@{utc='2026-10-22T13:22:12.5Z';category='host-phase';phase=9;code=172}
$recoveryChain=@($protectedStart,$protection,$retryStart,$attest,$hidden,$retryFailed)
$text=Get-CodexDiagnosticAnalysis (Report @($cancelled) $recoveryChain) $null
Check ($text.Contains('前一次启动记录了资源保护') -and $text.Contains('保留日志内未见它完成成功交接') -and $text.Contains('重复启动分支未找到可验证的目标窗口')) 'old-format report preserves protected startup context alongside latest replay failure'
Check ($text.Contains('后一次重复启动通过了身份验证') -and $text.Contains('已验证 8 个窗口，全部隐藏，合格主窗口 0 个') -and $text.Contains('不能据此确定是谁隐藏了窗口')) 'successful attestation and subsequent hidden counts remain observation rather than causation'
Check ($text.Contains('阶段 9、错误码 172') -and $text.Contains('报告未保留确切保护原因') -and -not $text.Contains('保护原因记录：系统内存压力')) 'phase nine cancellation and unknown old resource reason are reported honestly'
$exactReason=[pscustomobject]@{utc='2026-10-22T13:22:11.9Z';category='startup-resource-protection';resourceReason='system-memory-pressure'}
$text=Get-CodexDiagnosticAnalysis (Report @($cancelled) ($recoveryChain+@($exactReason))) $null
Check ($text.Contains('保护原因记录：系统内存压力') -and $text.Contains('不表示启动器自身内存超限') -and -not $text.Contains('报告未保留确切保护原因')) 'exact allowlisted trip reason survives later generic failure record'
$successfulHandoff=StartupEnd '2026-10-22T13:22:20Z'
Check (-not (Get-CodexDiagnosticAnalysis (Report @($cancelled) ($recoveryChain+@($successfulHandoff))) $null).Contains('前一次启动记录了资源保护')) 'intervening successful handoff retires historical protection context'
$newHost=[pscustomobject]@{utc='2026-10-22T13:22:20Z';category='host-phase';phase=20;code=0}
Check (-not (Get-CodexDiagnosticAnalysis (Report @($cancelled,$newHost) $recoveryChain) $null).Contains('前一次启动记录了资源保护')) 'new host boundary prevents assuming same-client failure chain'
$unknownReason=[pscustomobject]@{utc='2026-10-22T13:22:11.9Z';category='startup-resource-protection';resourceReason='system-memory-pressure PRIVATE C:\Users\SECRET'}
$text=Get-CodexDiagnosticAnalysis (Report @($cancelled) ($recoveryChain+@($unknownReason))) $null
Check ($text -notmatch 'PRIVATE|SECRET|C:\\Users' -and $text.Contains('报告未保留确切保护原因')) 'untrusted reason text never enters public analysis'
$badHidden=[pscustomobject]@{utc=$hidden.utc;category=$hidden.category;phase=4;verifiedWindows='8';visible=0;hidden=8;eligible=0}
$text=Get-CodexDiagnosticAnalysis (Report @($cancelled) @($protectedStart,$protection,$retryStart,$attest,$badHidden,$retryFailed)) $null
Check (-not $text.Contains('全部隐藏') -and $text.Contains('前一次启动记录了资源保护')) 'invalid hidden-window number cannot manufacture a hidden-state diagnosis'
$badAttest=[pscustomobject]@{utc='2026-10-22T13:22:25.55Z';category='replay-diagnostic';operation='attest';phase=5;reason=4}
Check (-not (Get-CodexDiagnosticAnalysis (Report @($cancelled) ($recoveryChain+@($badAttest))) $null).Contains('后一次重复启动通过了身份验证')) 'later failed attestation supersedes earlier success evidence'
$lateStart=StartupEntry '2026-10-22T14:22:25Z';$lateRetry=[pscustomobject]@{utc='2026-10-22T14:22:26Z';category='launcher-stage';stage='replay-target-unavailable-no-activation'}
Check (-not (Get-CodexDiagnosticAnalysis (Report @() @($protectedStart,$protection,$lateStart,$lateRetry)) $null).Contains('前一次启动记录了资源保护')) 'distant historical protection is not presented as recent context'
$r=Report @($cancelled) $recoveryChain;$r.logs+=$r.logs[1]
Check (-not (Get-CodexDiagnosticAnalysis $r $null).Contains('前一次启动记录了资源保护')) 'duplicated handoff sources do not prove recovery chronology'
$released=[pscustomobject]@{utc='2026-10-22T13:22:12.5Z';category='startup-resource-animation-released'}
$text=Get-CodexDiagnosticAnalysis (Report @() @($protectedStart,$exactReason,$released)) $null
Check ($text.Contains('本次启动记录了资源保护') -and $text.Contains('已请求保留壁纸准备并释放启动动画') -and $text.Contains('不能据此认定壁纸已恢复') -and -not $text.Contains('当时确认了首帧')) 'resource compensation request alone never proves wallpaper restoration'
# Synthetic timeline reproduces the two distinct protection layers in r8.
$memoryStart=StartupEntry '2026-10-22T16:04:21Z'
$memoryHostStart=[pscustomobject]@{utc='2026-10-22T16:04:22Z';category='host-phase';phase=20;code=0}
$memoryTrip=[pscustomobject]@{utc='2026-10-22T16:04:42.54Z';category='startup-resource-protection';resourceReason='system-memory-pressure'}
$memoryRelease=[pscustomobject]@{utc='2026-10-22T16:04:42.56Z';category='startup-resource-animation-released'}
$memoryFailed=[pscustomobject]@{utc='2026-10-22T16:04:42.56Z';category='host-phase';phase=10;code=174}
$memoryEnded=[pscustomobject]@{utc='2026-10-22T16:04:43Z';category='host-phase';phase=15;code=174}
$text=Get-CodexDiagnosticAnalysis (Report @($memoryHostStart,$memoryFailed,$memoryEnded) @($memoryStart,$memoryTrip,$memoryRelease)) $null
Check ($text.Contains('壁纸宿主因内存保护终止了本次准备') -and $text.Contains('错误码 174') -and $text.Contains('本次壁纸会话已停用') -and $text.Contains('已请求保留壁纸准备并释放启动动画')) 'host terminal memory failure is distinct from launcher release intent'
Check (-not $text.Contains('动画已受限') -and -not $text.Contains('当时确认了首帧')) 'host failure cannot be described as only limiting startup animation or successful playback'
$memoryRecovered=Event '2026-10-22T16:04:44Z' 'success'
$text=Get-CodexDiagnosticAnalysis (Report @($memoryFailed,$memoryEnded,$memoryRecovered) @($memoryStart)) $null
Check ($text.Contains('当时确认了首帧') -and -not $text.Contains('本次壁纸会话已停用')) 'later valid playing evidence supersedes historical memory failure'
$laterMemoryStart=StartupEntry '2026-10-22T16:05:00Z'
$text=Get-CodexDiagnosticAnalysis (Report @($memoryFailed,$memoryEnded) @($memoryStart,$laterMemoryStart)) $null
Check (-not $text.Contains('本次壁纸会话已停用') -and $text.Contains('没有可解释')) 'new launch boundary excludes old host memory failure'
$r=Report @() @($memoryEnded)
Check (-not (Get-CodexDiagnosticAnalysis $r $null).Contains('本次壁纸会话已停用')) 'host-looking failure in launcher log cannot prove terminal host state'
$badMemory=[pscustomobject]@{utc=$memoryEnded.utc;category='host-phase';phase=15;code='174 PRIVATE'}
$text=Get-CodexDiagnosticAnalysis (Report @($badMemory)) $null
Check (-not $text.Contains('本次壁纸会话已停用') -and -not $text.Contains('PRIVATE')) 'malformed host memory error code is not coerced or echoed'
$commitTrip=[pscustomobject]@{utc='2026-10-22T16:04:42.54Z';category='startup-resource-protection';resourceReason='system-commit-pressure'}
$text=Get-CodexDiagnosticAnalysis (Report @() @($memoryStart,$commitTrip,$memoryRelease)) $null
Check ($text.Contains('保护原因记录：可用提交内存余量不足') -and $text.Contains('不等于物理内存不足') -and -not $text.Contains('报告未保留确切保护原因')) 'new commit pressure is distinguished from historical physical pressure'
$hostCommitTrip=[pscustomobject]@{utc='2026-10-22T16:04:42.54Z';category='startup-resource-protection';resourceReason='wallpaper-commit-pressure'}
$text=Get-CodexDiagnosticAnalysis (Report @() @($memoryStart,$hostCommitTrip,$memoryRelease)) $null
Check ($text.Contains('保护原因记录：当前壁纸宿主报告提交内存保护暂停') -and $text.Contains('不同于启动器自身采样触发') -and -not $text.Contains('报告未保留确切保护原因') -and -not $text.Contains('当时确认了首帧')) 'fresh host pressure release reason is not mistaken for launcher sampling or playback'
$suspended=[pscustomobject]@{utc='2026-10-22T16:04:42Z';category='host-phase';phase=43;code=0;pressureState='suspended';availableCommitBytes=536870911;totalCommitBytes=34359738368}
$resuming=[pscustomobject]@{utc='2026-10-22T16:04:44Z';category='host-phase';phase=43;code=0;pressureState='resume-requested';availableCommitBytes=1073741824;totalCommitBytes=34359738368}
$text=Get-CodexDiagnosticAnalysis (Report @($suspended) @($memoryStart)) $null
Check ($text.Contains('壁纸宿主已请求内存保护暂停') -and $text.Contains('不属于错误码 174 的终态停用') -and -not $text.Contains('当时确认了首帧')) 'recoverable pressure pause never becomes permanent host failure or success'
$text=Get-CodexDiagnosticAnalysis (Report @($suspended,$resuming) @($memoryStart)) $null
Check ($text.Contains('壁纸宿主已请求恢复') -and $text.Contains('不能把恢复请求视为播放成功') -and -not $text.Contains('当时确认了首帧')) 'resume request needs later actual playback proof'
$text=Get-CodexDiagnosticAnalysis (Report @($suspended,$resuming,(Event '2026-10-22T16:04:45Z' 'success')) @($memoryStart)) $null
Check ($text.Contains('当时确认了首帧') -and -not $text.Contains('已请求恢复')) 'later valid playback evidence retires recovery request status'
$badPressure=[pscustomobject]@{utc='2026-10-22T16:04:46Z';category='host-phase';phase=43;code=0;pressureState='resume-requested PRIVATE'}
$text=Get-CodexDiagnosticAnalysis (Report @($resuming,$badPressure)) $null
Check ($text.Contains('没有可解释的本次播放证明') -and -not $text.Contains('已请求恢复') -and -not $text.Contains('PRIVATE')) 'unknown newer host pressure state does not reuse older resume evidence'
$text=Get-CodexDiagnosticAnalysis (Report @($suspended) @($laterMemoryStart)) $null
Check (-not $text.Contains('已请求内存保护暂停')) 'new launch boundary retires previous pressure request'
$pressureSnapshot=[pscustomobject]@{utc='2026-10-22T16:04:44Z';category='host-phase';phase=39;code=0;generationMatches=1;protocolVersion=2;health='fresh';attached=1;disposed=0;memoryPressure=1;actualPhase='hibernating';mediaReady=0;playing=0;layoutSupported=1}
$text=Get-CodexDiagnosticAnalysis (Report @($suspended,$pressureSnapshot)) $null
Check ($text.Contains('壁纸渲染器的最新快照仍处于内存保护暂停') -and -not $text.Contains('没有可解释的本次播放证明')) 'fresh renderer pressure snapshot retains suspended-state explanation'
foreach($field in @('generationMatches','protocolVersion','attached')){
    $value=$pressureSnapshot.$field;$pressureSnapshot.$field=0
    Check (-not (Get-CodexDiagnosticAnalysis (Report @($pressureSnapshot)) $null).Contains('最新快照仍处于内存保护暂停')) ('unverified snapshot cannot prove pressure pause: '+$field)
    $pressureSnapshot.$field=$value
}
$pressureSnapshot.health='stale'
Check (-not (Get-CodexDiagnosticAnalysis (Report @($pressureSnapshot)) $null).Contains('最新快照仍处于内存保护暂停')) 'stale pressure snapshot cannot prove current suspension'
$pressureSnapshot.health='fresh';$pressureSnapshot.disposed=1
Check ((Get-CodexDiagnosticAnalysis (Report @($pressureSnapshot)) $null).Contains('壁纸渲染已结束')) 'terminal disposed state overrides pressure flag'
# Public entry can fail before the child emits a launcher-entry boundary.
# These logs do not identify --check / --configure / --preview vs normal start.
function PublicEntry([string]$Time,[string]$Code,$ExitCode=76) {
    $value=[pscustomobject]@{utc=$Time;category='public-entry';publicCode=$Code}
    if($Code -ceq 'PUBLIC_CHILD_EXIT'){$value|Add-Member -NotePropertyName exitCode -NotePropertyValue $ExitCode}
    return $value
}
function WithPublic($Report,$Entries) {
    $Report.logs+= [pscustomobject]@{log='diagnostics/portable-launch.log';records=@($Entries)}
    return $Report
}
$entryOldPlaying=Event '2026-10-24T10:00:00Z' 'success'
$entryFailure=PublicEntry '2026-10-24T11:00:00Z' 'PUBLIC_CODE_INTEGRITY_FAILED'
$text=Get-CodexDiagnosticAnalysis (WithPublic (Report @($entryOldPlaying)) @($entryFailure)) $null
Check ($text.Contains('公开入口操作异常') -and $text.Contains('PUBLIC_CODE_INTEGRITY_FAILED') -and -not $text.Contains('当时确认了首帧')) 'public preparation failure before launcher boundary cannot hide behind old playback proof'
Check ($text.Contains('启动、预览或校验') -and $text.Contains('不能据此认定当前客户端或壁纸已经关闭')) 'public entry failure does not invent mode or terminate state'
$entryStillPlaying=Event '2026-10-24T11:00:01Z' 'success'
$text=Get-CodexDiagnosticAnalysis (WithPublic (Report @($entryOldPlaying,$entryStillPlaying)) @($entryFailure)) $null
Check ($text.Contains('公开入口操作异常') -and -not $text.Contains('当时确认了首帧')) 'continuing existing host cannot erase separate public entry failure'
$entryStart=StartupEntry '2026-10-24T11:00:02Z'
$text=Get-CodexDiagnosticAnalysis (WithPublic (Report @($entryOldPlaying) @($entryStart)) @($entryFailure)) $null
Check (-not $text.Contains('公开入口操作异常') -and -not $text.Contains('PUBLIC_CODE_INTEGRITY_FAILED')) 'later real launcher boundary retires older public failure'
$entryNewHost=[pscustomobject]@{utc='2026-10-24T11:00:02Z';category='host-phase';phase=20;code=0}
$text=Get-CodexDiagnosticAnalysis (WithPublic (Report @($entryOldPlaying,$entryNewHost,(Event '2026-10-24T11:00:03Z' 'success'))) @($entryFailure)) $null
Check (-not $text.Contains('公开入口操作异常') -and $text.Contains('当时确认了首帧')) 'new host and new playback supersede old public error'
$entrySameStart=StartupEntry '2026-10-24T11:00:00Z'
$text=Get-CodexDiagnosticAnalysis (WithPublic (Report @($entryStillPlaying) @($entrySameStart)) @($entryFailure)) $null
Check ($text.Contains('公开入口记录的先后关系无法确认') -and -not $text.Contains('当时确认了首帧') -and -not $text.Contains('公开入口操作异常：')) 'same-time real boundary does not manufacture ordering'
$entryOtherFailure=PublicEntry '2026-10-24T11:00:00Z' 'PUBLIC_MEDIA_INCOMPLETE'
$text=Get-CodexDiagnosticAnalysis (WithPublic (Report @($entryOldPlaying)) @($entryFailure,$entryOtherFailure)) $null
Check ($text.Contains('公开入口记录的先后关系无法确认') -and -not $text.Contains('当时确认了首帧')) 'different public results at same latest timestamp stay ambiguous'
$text=Get-CodexDiagnosticAnalysis (WithPublic (Report @($entryOldPlaying)) @($entryFailure,$entryFailure)) $null
Check ($text.Contains('公开入口操作异常') -and -not $text.Contains('先后关系无法确认')) 'identical duplicated public records are idempotent'
foreach($time in @('','invalid PRIVATE','2026-10-24T11:00:99Z')){
    $text=Get-CodexDiagnosticAnalysis (WithPublic (Report @($entryOldPlaying)) @((PublicEntry $time 'PUBLIC_CODE_INTEGRITY_FAILED'))) $null
    Check ($text.Contains('公开入口记录的先后关系无法确认') -and -not $text.Contains('当时确认了首帧') -and -not $text.Contains('PRIVATE')) 'untimed recognized error cannot establish latest chronology or preserve assumed success'
}
$entryWithoutTime=[pscustomobject]@{category='public-entry';publicCode='PUBLIC_LAUNCH_FAILED'}
Check ((Get-CodexDiagnosticAnalysis (WithPublic (Report) @($entryWithoutTime)) $null).Contains('公开入口记录的先后关系无法确认')) 'missing timestamp remains uncorrelated'
$entryBusy=PublicEntry '2026-10-24T11:00:01Z' 'PUBLIC_ENTRY_BUSY'
$entryZero=PublicEntry '2026-10-24T11:00:01Z' 'PUBLIC_CHILD_EXIT' 0
foreach($neutral in @($entryBusy,$entryZero)){
    $text=Get-CodexDiagnosticAnalysis (WithPublic (Report @($entryOldPlaying)) @($neutral)) $null
    Check (-not $text.Contains('公开入口操作异常') -and -not $text.Contains('公开入口记录的先后关系无法确认')) 'busy or child exit zero alone is not an error or playback proof'
    $text=Get-CodexDiagnosticAnalysis (WithPublic (Report @($entryOldPlaying)) @($entryFailure,$neutral)) $null
    Check ($text.Contains('PUBLIC_CODE_INTEGRITY_FAILED') -and -not $text.Contains('当时确认了首帧')) 'neutral public result cannot erase an earlier error without true startup boundary'
}
foreach($exit in @(-2147483648,-1,1,76,2147483647)){
    $text=Get-CodexDiagnosticAnalysis (WithPublic (Report @($entryOldPlaying)) @((PublicEntry '2026-10-24T11:00:00Z' 'PUBLIC_CHILD_EXIT' $exit))) $null
    Check ($text.Contains('PUBLIC_CHILD_EXIT') -and $text.Contains(('exitCode='+$exit)) -and -not $text.Contains('当时确认了首帧')) 'bounded nonzero signed child code retained as entry evidence'
}
foreach($exit in @('76 PRIVATE',0.5,2147483648L,$null)){
    $text=Get-CodexDiagnosticAnalysis (WithPublic (Report @($entryOldPlaying)) @((PublicEntry '2026-10-24T11:00:00Z' 'PUBLIC_CHILD_EXIT' $exit))) $null
    Check (-not $text.Contains('公开入口操作异常') -and -not $text.Contains('PRIVATE')) 'malformed child code not coerced or echoed'
}
$entryUnknown=PublicEntry '2026-10-24T11:00:00Z' 'PUBLIC_CODE_INTEGRITY_FAILED PRIVATE C:\Users\SECRET'
$text=Get-CodexDiagnosticAnalysis (WithPublic (Report @($entryOldPlaying)) @($entryUnknown)) $null
Check (-not $text.Contains('公开入口操作异常') -and $text -notmatch 'PRIVATE|SECRET') 'public code is an exact fixed allowlist'
$text=Get-CodexDiagnosticAnalysis (Report @($entryFailure,$entryOldPlaying)) $null
Check (-not $text.Contains('公开入口操作异常')) 'host log cannot impersonate public entry source'
$entryWrongCategory=[pscustomobject]@{utc=$entryFailure.utc;category='launcher-stage';publicCode=$entryFailure.publicCode}
Check (-not (Get-CodexDiagnosticAnalysis (WithPublic (Report) @($entryWrongCategory)) $null).Contains('公开入口操作异常')) 'public log requires public-entry category'
$entrySameZero=PublicEntry $entryFailure.utc 'PUBLIC_CHILD_EXIT' 0
$text=Get-CodexDiagnosticAnalysis (WithPublic (Report @($entryOldPlaying)) @($entryFailure,$entrySameZero)) $null
Check ($text.Contains('公开入口记录的先后关系无法确认') -and -not $text.Contains('当时确认了首帧')) 'same-time neutral and failed public results cannot be ordered'
$r=WithPublic (Report @($entryOldPlaying)) @($entryFailure)
$r.logs+=[pscustomobject]@{log='unknown-source';records=@($entryStart,$entryNewHost)}
Check ((Get-CodexDiagnosticAnalysis $r $null).Contains('PUBLIC_CODE_INTEGRITY_FAILED')) 'unknown log cannot fabricate a newer boundary'
$entryMode=PublicEntry $entryFailure.utc 'PUBLIC_CODE_INTEGRITY_FAILED'
$entryMode|Add-Member -NotePropertyName mode -NotePropertyValue 'preview PRIVATE_SECRET'
$text=Get-CodexDiagnosticAnalysis (WithPublic (Report @($entryOldPlaying)) @($entryMode)) $null
Check ($text.Contains('没有运行模式标记') -and -not $text.Contains('PRIVATE_SECRET')) 'untrusted added mode never attributes entry failure to preview or normal start'
$r=WithPublic (Report @($entryOldPlaying)) @($entryFailure);$r.logs+=$r.logs[-1]
$text=Get-CodexDiagnosticAnalysis $r $null
Check ($text.Contains('公开入口记录的先后关系无法确认') -and -not $text.Contains('当时确认了首帧')) 'duplicate public sources do not prove a single latest operation'
foreach($code in @('PUBLIC_PREPARATION_TIMEOUT','PUBLIC_LAUNCH_FAILED','PUBLIC_CHILD_TIMEOUT','PUBLIC_COMPONENT_OR_SIGNATURE_REJECTED','PUBLIC_PREPARATION_FAILED','PUBLIC_UNSUPPORTED_WINDOWS','PUBLIC_CODE_INTEGRITY_FAILED','PUBLIC_MEDIA_INCOMPLETE','PUBLIC_LAUNCHER_CONFIG_INVALID','PUBLIC_LAUNCHER_ASSET_INVALID','PUBLIC_OFFICIAL_CODEX_NOT_INSTALLED','PUBLIC_PACKAGE_ENUMERATION_FAILED','PUBLIC_SUPPORTED_CODEX_EXECUTABLE_NOT_FOUND','PUBLIC_OFFICIAL_DISCOVERY_FAILED','PUBLIC_OFFICIAL_IDENTITY_REJECTED','PUBLIC_OFFICIAL_RUNTIME_UNSUPPORTED')){
    $text=Get-CodexDiagnosticAnalysis (WithPublic (Report) @((PublicEntry '2026-10-24T11:00:00Z' $code))) $null
    Check ($text.Contains('公开入口操作异常') -and $text.Contains($code)) ('public fixed failure classification '+$code)
}
$text=Get-CodexDiagnosticAnalysis (WithPublic (Report @() $replayStages) @((PublicEntry '2026-10-22T12:28:49Z' 'PUBLIC_CHILD_EXIT' 76))) $null
Check ($text.Contains('重复启动分支未找到可验证的目标窗口') -and $text.Contains('PUBLIC_CHILD_EXIT')) 'entry exit preserves specific replay cause rather than replacing it'
$receipt=[ordered]@{passed=$true;checks=$checks;scope='Pure synthetic diagnostics; no filesystem media inspection, process launch, GPU test or network.'}
[IO.File]::WriteAllText((Join-Path $WorkRoot 'results.json'),($receipt|ConvertTo-Json),[Text.UTF8Encoding]::new($false))
Write-Output ('ALL PASS: diagnostic analysis checks='+$checks)
