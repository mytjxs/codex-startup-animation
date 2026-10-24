Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$script:PortableReportToolsRoot=[IO.Path]::GetDirectoryName($PSCommandPath)

function Stop-Fix([string]$Code) { throw [IO.InvalidDataException]::new('FCP-'+$Code) }

function Assert-FixPowerShell {
    if($PSVersionTable.PSVersion.Major -ne 5 -or $PSVersionTable.PSVersion.Minor -lt 1){Stop-Fix 'USE-WINDOWS-POWERSHELL-51'}
}

function Assert-FixPath([string]$Path,[switch]$Directory,[switch]$File) {
    if([string]::IsNullOrWhiteSpace($Path) -or $Path -notmatch '^[A-Za-z]:[\\/]'){Stop-Fix 'LOCAL-ABSOLUTE-PATH-REQUIRED'}
    $full=[IO.Path]::GetFullPath($Path)
    if($full.StartsWith('\\') -or $full.Substring(2).Contains(':')){Stop-Fix 'UNSAFE-PATH'}
    if(([IO.DriveInfo]::new([IO.Path]::GetPathRoot($full))).DriveType -eq [IO.DriveType]::Network){Stop-Fix 'NETWORK-DRIVE'}
    for($part=$full;$part;$part=[IO.Path]::GetDirectoryName($part)) {
        if(Test-Path -LiteralPath $part){
            $item=Get-Item -LiteralPath $part -Force
            if(($item.Attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){Stop-Fix 'REPARSE-PATH'}
        }
    }
    if($Directory -and -not (Test-Path -LiteralPath $full -PathType Container)){Stop-Fix 'DIRECTORY-MISSING'}
    if($File -and -not (Test-Path -LiteralPath $full -PathType Leaf)){Stop-Fix 'FILE-MISSING'}
    return $full
}

function Join-FixPath([string]$Root,[string]$Relative) {
    if([string]::IsNullOrEmpty($Relative) -or $Relative -cnotmatch '^[A-Za-z0-9.][A-Za-z0-9._/ -]*$'){Stop-Fix 'MEMBER-PATH'}
    foreach($part in $Relative.Split('/')) {
        if($part -eq '' -or $part -eq '.' -or $part -eq '..' -or $part.StartsWith(' ') -or $part.EndsWith('.') -or $part.EndsWith(' ') -or $part -match '^(?i:CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)'){Stop-Fix 'MEMBER-PATH'}
    }
    $base=(Assert-FixPath $Root -Directory).TrimEnd('\')
    $full=Assert-FixPath ([IO.Path]::Combine($base,$Relative.Replace('/','\')))
    if(-not $full.StartsWith($base+'\',[StringComparison]::OrdinalIgnoreCase)){Stop-Fix 'PATH-ESCAPE'}
    return $full
}

function Test-FixInteger($Value,[long]$Minimum,[long]$Maximum) {
    return (($Value -is [int] -or $Value -is [long]) -and $Value -ge $Minimum -and $Value -le $Maximum)
}

function Assert-FixRecords([string]$Root,$Records,[int]$Count,[switch]$Patch) {
    $items=@($Records)
    if($items.Count -ne $Count -or $Count -lt 1 -or $Count -gt 300){Stop-Fix 'FILE-COUNT'}
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($record in $items) {
        if($record.path -isnot [string] -or $record.sha256 -isnot [string] -or $record.sha256 -cnotmatch '^[A-F0-9]{64}$' -or -not (Test-FixInteger $record.bytes 0 4294967296)){Stop-Fix 'FILE-RECORD'}
        [void](Join-FixPath $Root $record.path)
        if(-not $seen.Add($record.path)){Stop-Fix 'DUPLICATE-MEMBER'}
        if($record.path -imatch '(^|/)(wallpaper-host-config\.json|wallpaper-host-session\.json|test-copy-receipt\.json|TEST-COPY-INCOMPLETE\.txt|Collect-Fixed-TestReport\.ps1|TestCopy\.Common\.ps1)$' -or $record.path -imatch '(^|/)(CodexDiagnosticReports|isolated-[^/]*)(/|$)' -or $record.path -imatch '\.(log|lock)(\.[0-9]+)?$'){Stop-Fix 'RUNTIME-MEMBER-FORBIDDEN'}
        if(-not $Patch -and $record.path -ieq 'release-manifest.json'){Stop-Fix 'SELF-MANIFEST'}
    }
}

function Get-FixEventToken([string]$Body,[string]$Key) {
    # Count malformed duplicates too. A valid value beside an invalid duplicate
    # is ambiguous and must not become a misleading successful observation.
    $found=[regex]::Matches($Body,'(?:^|[\s;])'+[regex]::Escape($Key)+'=([^\s;]*)(?=$|[\s;])')
    if($found.Count -eq 1){return $found[0].Groups[1].Value}
    return $null
}
function Get-FixEventNumber([string]$Body,[string]$Key,[long]$Maximum,[long]$Minimum=-1) {
    $token=Get-FixEventToken $Body $Key;$number=0L
    if($null -ne $token -and $token -cmatch '^-?[0-9]{1,15}$' -and [long]::TryParse($token,[ref]$number) -and $number -ge $Minimum -and $number -le $Maximum){return $number}
    return -1L
}
function Get-FixEventEnum([string]$Body,[string]$Key,[string[]]$Known) {
    $token=Get-FixEventToken $Body $Key
    if($null -ne $token -and $token -cin $Known){return $token}
    return 'unknown'
}
function Get-FixRelativeNumber([string]$Body,[string]$Key,[long]$Magnitude) {
    # -1 is a valid relative edge; use an explicit unknown value instead.
    $token=Get-FixEventToken $Body $Key;$number=0L
    if($null -ne $token -and $token -cmatch '^-?[0-9]{1,15}$' -and [long]::TryParse($token,[ref]$number) -and $number -ge -$Magnitude -and $number -le $Magnitude){return $number}
    return -2147483648L
}
function Get-FixEventBoolean([string]$Body,[string]$Key) {
    $token=Get-FixEventToken $Body $Key
    if($token -ceq 'True'){return 1L};if($token -ceq 'False'){return 0L};return -1L
}
function Get-FixEventSecondsMs([string]$Body,[string]$Key) {
    $token=Get-FixEventToken $Body $Key
    if($null -ne $token -and $token -cmatch '^[0-9]{1,8}\.[0-9]{3}$'){
        $parts=$token.Split('.');$milliseconds=[long]$parts[0]*1000+[long]$parts[1]
        if($milliseconds -le 31536000000){return $milliseconds}
    }
    return -1L
}
function Add-FixSnapshotFields($Fields,[string]$Body,[switch]$Runtime) {
    foreach($key in @('attached','mediaReady','prepared','disposed','playing','wallpaperApplied','revealed','layoutSupported','firstFrameConfirmed','documentHidden','stageVisible','recoveryNeeded','leaseNeedsRenew','layoutSuspended','memoryPressure','manuallyPaused','videoPaused','videoSeeking','playStarted','frameCallbackPending','preparationTimerPending','frozen','generationMatches')){$Fields[$key]=Get-FixEventNumber $Body $key 1}
    if($Runtime){$Fields['commitAccepted']=Get-FixEventNumber $Body 'commitAccepted' 1}
    $numbers=[ordered]@{videoReadyState=4;videoErrorCode=4;videoNetworkState=3;mediaTimeMs=86400000;mediaGeneration=2147483647;presentedFrames=2147483647;protocolVersion=2}
    foreach($key in $numbers.Keys){$Fields[$key]=Get-FixEventNumber $Body $key $numbers[$key]}
    $Fields['rendererCode']=Get-FixEventEnum $Body 'rendererCode' @('idle','loading','prepared','playing','reveal-pending','ready-hidden','resuming','paused','hibernating','disposed','unsupported-layout','unverified','media-error','play-rejected','first-frame-timeout','reveal-timeout','resume-timeout','playback-stalled','invalid-file','file-too-large','retired','recovery-budget-exhausted')
    $Fields['actualPhase']=Get-FixEventEnum $Body 'actualPhase' @('retired','faulted','hibernating','paused','resuming','gap','preparing','prepared','visible')
    $Fields['health']=Get-FixEventEnum $Body 'health' @('faulted','stale','fresh')
    $Fields['layoutReason']=Get-FixEventEnum $Body 'layoutReason' @('not-checked','root-missing','main-count','main-token','workspace-shape','shell-shape','composer-ambiguous','frame-count','frame-token','frame-row','frame-shape','ready')
}
function Add-FixExceptionFields($Fields,[string]$Body) {
    $Fields['exceptionType']=Get-FixEventEnum $Body 'exceptionType' @('None','OtherException','InvalidOperationException','ArgumentException','ArgumentOutOfRangeException','TimeoutException','IOException','InvalidDataException','EndOfStreamException','FileNotFoundException','DirectoryNotFoundException','UnauthorizedAccessException','Win32Exception','SecurityException','SerializationException','XmlException','ObjectDisposedException','AggregateException')
    $Fields['nativeErrorCode']=Get-FixEventNumber $Body 'nativeErrorCode' 2147483647 0
    $Fields['hresult']=Get-FixEventNumber $Body 'hresult' 2147483647 -2147483648
}
function Convert-FixHandoff([string]$Body,[string]$Utc) {
    $fields=[ordered]@{utc=$Utc}
    if($Body.StartsWith('Startup resource protection: ',[StringComparison]::Ordinal)){
        $value=$Body.Substring('Startup resource protection: '.Length)
        $reason=if($value -cin @('system-memory-pressure','system-commit-pressure','wallpaper-commit-pressure','startup-private-memory-limit','startup-sustained-cpu-limit','startup-resource-monitor-unavailable')){$value}else{'unknown'}
        return [pscustomobject]@{utc=$Utc;category='startup-resource-protection';resourceReason=$reason}
    }
    if($Body -ceq 'Startup animation released for resource protection; wallpaper preparation continues; no success finale'){
        return [pscustomobject]@{utc=$Utc;category='startup-resource-animation-released'}
    }
    if($Body.StartsWith('replay-native-candidates phase=',[StringComparison]::Ordinal)){
        $fields.category='replay-native-candidates';$fields.phase=Get-FixEventNumber $Body 'phase' 4 1
        foreach($key in @('verifiedWindows','visible','hidden','cloaked','eligible','minimized','ownedRejected','childRejected','toolRejected','cloakedRejected','rectUnavailable','sizeRejected','stateChanged')){$fields[$key]=Get-FixEventNumber $Body $key 99999 0}
        return [pscustomobject]$fields
    }
    if($Body.StartsWith('replay-diagnostic operation=',[StringComparison]::Ordinal)){
        $fields.category='replay-diagnostic';$fields.operation=Get-FixEventEnum $Body 'operation' @('find','attest')
        $fields.phase=Get-FixEventNumber $Body 'phase' 16 0
        $fields.reason=Get-FixEventNumber $Body 'reason' 32 0
        $fields.elapsedMs=Get-FixEventNumber $Body 'elapsedMs' 31536000000 0
        Add-FixExceptionFields $fields $Body
        return [pscustomobject]$fields
    }
    $nativeWait=$Body.StartsWith('Native readiness waiting after placement; ',[StringComparison]::Ordinal)
    $placementWait=$Body.StartsWith('Compact placement waiting; ',[StringComparison]::Ordinal)
    if($Body.StartsWith('Native geometry; ',[StringComparison]::Ordinal)){
        $fields.category='native-geometry';$fields.phase=Get-FixEventNumber $Body 'phase' 2
        foreach($key in @('coverRectKnown','targetRectKnown','frameKnown','targetVisible','targetMinimized','targetCloaked')){$fields[$key]=Get-FixEventNumber $Body $key 1}
        foreach($key in @('targetDpi','coverDpi')){$fields[$key]=Get-FixEventNumber $Body $key 768 48}
        foreach($key in @('coverWidth','coverHeight','targetWidth','targetHeight')){$fields[$key]=Get-FixEventNumber $Body $key 65536 1}
        foreach($key in @('deltaLeft','deltaTop','deltaRight','deltaBottom')){$fields[$key]=Get-FixRelativeNumber $Body $key 65536}
        foreach($key in @('frameInsetLeft','frameInsetTop','frameInsetRight','frameInsetBottom')){$fields[$key]=Get-FixRelativeNumber $Body $key 1024}
        return [pscustomobject]$fields
    }
    if($Body.StartsWith('Native hidden maximized placement confirmed; ',[StringComparison]::Ordinal)){
        $fields.category='native-hidden-maximized-placement'
        foreach($key in @('originalShow','capturedShow','currentShow')){$fields[$key]=Get-FixEventNumber $Body $key 11 0}
        $fields.lateMaximized=Get-FixEventBoolean $Body 'lateMaximized'
        return [pscustomobject]$fields
    }
    if($nativeWait -or $placementWait){
        $fields.category=if($nativeWait){'native-readiness-wait'}else{'compact-placement-wait'}
        $fields.phase=Get-FixEventNumber $Body 'phase' 2
        foreach($key in @('elapsedMs','probeAgeMs','postReadyLossMs')){$fields[$key]=Get-FixEventNumber $Body $key 31536000000}
        foreach($key in @('exists','verified','visible','minimized','cloaked','mainCandidate','sameProbe','probeResponsive','probePending')){$fields[$key]=Get-FixEventBoolean $Body $key}
        return [pscustomobject]$fields
    }
    if($Body.StartsWith('Startup resource sample; ',[StringComparison]::Ordinal)){
        $fields.category='startup-resource-sample'
        $limits=[ordered]@{elapsedMs=31536000000;intervalMs=31536000000;cpuPermille=1000;privateBytes=281474976710656;availablePhysicalBytes=281474976710656;totalPhysicalBytes=281474976710656;availableCommitBytes=281474976710656;totalCommitBytes=281474976710656;logicalProcessors=4096;lowMemory=1;notificationLow=1}
        foreach($key in $limits.Keys){$fields[$key]=Get-FixEventNumber $Body $key $limits[$key]}
        return [pscustomobject]$fields
    }
    if($Body.StartsWith('Living preparation gates; ',[StringComparison]::Ordinal)){
        $fields.category='living-preparation-gates';$fields.elapsedMs=Get-FixEventNumber $Body 'elapsedMs' 31536000000
        foreach($key in @('nativeStable','focusSettled','targetForeground','ownOverlayActive','filmWpfActive','filmNativeForeground','pulseWpfActive','pulseNativeForeground','wallpaperPrepared','pulseReady','finaleReady')){$fields[$key]=Get-FixEventBoolean $Body $key}
        $fields.foreground=Get-FixEventEnum $Body 'foreground' @('none','film','pulse','target','shell','own-other','other')
        $fields.focusAttempts=Get-FixEventNumber $Body 'focusAttempts' 2147483647
        $fields.mediaCode=Get-FixEventNumber $Body 'mediaCode' 65535;$fields.refreshCode=Get-FixEventNumber $Body 'refreshCode' 4
        return [pscustomobject]$fields
    }
    if($Body.StartsWith('Living finale committed; ',[StringComparison]::Ordinal)){
        $fields.category='living-finale-committed'
        foreach($key in @('nativeStable','focusSettled','actualForeground','existingWallpaperPreserved','wallpaperReady','finalePrerolled','pulseReady')){$fields[$key]=Get-FixEventBoolean $Body $key}
        $fields.speed=Get-FixEventNumber $Body 'speed' 1 1;return [pscustomobject]$fields
    }
    if($Body.StartsWith('Handoff complete; ',[StringComparison]::Ordinal)){
        $fields.category='handoff-complete'
        foreach($key in @('success','wallpaperCommitConfirmed','wallpaperPlaybackRequiresObserver','replayExisting')){$fields[$key]=Get-FixEventBoolean $Body $key}
        $fields.pulseFrames=Get-FixEventNumber $Body 'pulseFrames' 2147483647;return [pscustomobject]$fields
    }
    if($Body.StartsWith('Pulse reveal begins; ',[StringComparison]::Ordinal)){
        $fields.category='pulse-reveal-begins';$fields.targetReady=Get-FixEventBoolean $Body 'targetReady';$fields.focusPrepared=Get-FixEventBoolean $Body 'focusPrepared'
        $fields.waitMs=Get-FixEventSecondsMs $Body 'waitSeconds';return [pscustomobject]$fields
    }
    if($Body.StartsWith('Opaque pulse pre-rendered behind playing movie; ',[StringComparison]::Ordinal)){
        $fields.category='pulse-prerendered';$fields.coverCycles=Get-FixEventNumber $Body 'coverCycles' 2147483647
        $fields.filmPositionMs=Get-FixEventSecondsMs $Body 'filmPosition';return [pscustomobject]$fields
    }
    if($Body.StartsWith('Living wallpaper preparation evidence=',[StringComparison]::Ordinal)){
        $fields.category='living-preparation-evidence';$fields.evidence=Get-FixEventEnum $Body 'evidence' @('existing-wallpaper-preserved-not-reprepared','decoded-frame-confirmed','hidden-decoder-ready');return [pscustomobject]$fields
    }
    if($Body.StartsWith('bridge-diagnostic trace=',[StringComparison]::Ordinal)){
        $fields.category='bridge-diagnostic';$fields.operation=Get-FixEventEnum $Body 'operation' @('discover','open')
        Add-FixExceptionFields $fields $Body
        foreach($key in @('phase','reason')){$fields[$key]=Get-FixEventNumber $Body $key 16}
        $fields.elapsedMs=Get-FixEventNumber $Body 'elapsedMs' 31536000000;$fields.disposition=Get-FixEventNumber $Body 'disposition' 3;$fields.mediaCode=Get-FixEventNumber $Body 'mediaCode' 65535
        $fields.outcome=Get-FixEventEnum $Body 'outcome' @('none','wallpaper-disabled','bridge-reused','bridge-discovery-normal-fallback','bridge-unknown-no-second-launch','wallpaper-unavailable-existing-process','wallpaper-host-missing','wallpaper-host-start-failed','bridge-started','bridge-not-started-normal-fallback','bridge-existing-normal-reuse','activation-failed')
        return [pscustomobject]$fields
    }
    $stage=[regex]::Match($Body,'\Astartup-stage=(?<stage>launcher-entry|launcher-validated|launcher-create-film|launcher-show-film|replay-target-unavailable-no-activation|replay-existing-window-pinned) pid=\d{1,10} elapsedMs=(?<elapsed>\d{1,11})\z')
    if($stage.Success -and [long]$stage.Groups['elapsed'].Value -le 31536000000){return [pscustomobject]@{utc=$Utc;category='launcher-stage';stage=$stage.Groups['stage'].Value;elapsedMs=[long]$stage.Groups['elapsed'].Value}}
    $commandFailure=[regex]::Match($Body,'\AWallpaper bridge command failed: (?<command>reveal|pause|disable|status|refresh|prepare|commit)\z')
    if($commandFailure.Success){return [pscustomobject]@{utc=$Utc;category='wallpaper-command-failed';command=$commandFailure.Groups['command'].Value}}
    $exact=@{
        'Living intro started; speed=1; maximumVideoSurfaces=2'='living-intro-started'
        'Living idle entered; speed=1'='living-idle-entered'
        'Living playback released; videoSurfaces=0'='living-playback-released'
        'Cleanup complete: decoder, local pulse and timers released; no desktop shield'='launcher-cleanup-complete'
        'Animation cancelled; restoring target placement'='animation-cancelled'
        'Native recovery still unconfirmed after bounded retry'='native-recovery-unconfirmed'
        'Animation explicitly skipped; wallpaper preparation continues; no success finale'='animation-skipped'
        'Wallpaper host process ended during startup; stale preparation discarded'='wallpaper-host-exited'
        'Movie hidden beneath prepared cover; decoder retained through reveal'='movie-hidden-for-reveal'
        'Input guard unavailable at impact; bounded fallback'='impact-input-guard-unavailable'
        'Pulse unavailable at impact; bounded fallback'='impact-pulse-unavailable'
        'Pulse initial layout timeout'='pulse-layout-timeout'
        'Cannot raise prepared cover'='pulse-cover-unavailable'
    }
    foreach($key in $exact.Keys){if($Body -ceq $key){return [pscustomobject]@{utc=$Utc;category=$exact[$key]}}}
    $opened=[regex]::Match($Body,'\ALiving media opened (?<role>intro|idle|finale); duration=(?<seconds>\d{1,2})\.(?<fraction>\d{3})\z')
    if($opened.Success){$duration=[int]$opened.Groups['seconds'].Value*1000+[int]$opened.Groups['fraction'].Value;if($duration -ge 100 -and $duration -le 30000){return [pscustomobject]@{utc=$Utc;category='living-media-opened';role=$opened.Groups['role'].Value;durationMs=$duration}}}
    $loop=[regex]::Match($Body,'\ALiving idle loop (?<count>\d{1,10}); speed=1\z')
    if($loop.Success -and [long]$loop.Groups['count'].Value -ge 1 -and [long]$loop.Groups['count'].Value -le [int]::MaxValue){return [pscustomobject]@{utc=$Utc;category='living-idle-loop';loopCount=[int]$loop.Groups['count'].Value}}
    $failure=$Body;$livingFailure=$failure.StartsWith('Living preparation failed: ',[StringComparison]::Ordinal)
    if($livingFailure){$failure=$failure.Substring(27)}
    $failures=@{
        '启动素材时长或尺寸无效'='startup-media-invalid'
        '落泪结束前未完成客户端交接'='living-handoff-incomplete'
        '启动视频解码器未能就绪'='startup-media-open-timeout'
        '启动视频解码停止超过五秒'='startup-media-stalled'
        '下一段启动视频未能加载'='startup-next-media-unavailable'
        '壁纸宿主已退出 本次结束启动动画并保留客户端'='wallpaper-host-exited'
        '客户端窗口准备失败 已保留 Codex 请查看启动日志'='client-window-preparation-failed'
        '启动准备失败 已保留 Codex 请查看启动日志'='startup-preparation-failed'
        '未找到可用的壁纸会话 本次没有播放成功转场'='wallpaper-session-unavailable'
        '壁纸准备失败 本次没有播放成功转场'='wallpaper-preparation-failed'
        '准备证据已过期 本次普通显示 Codex'='preparation-evidence-expired'
        '等待客户端或壁纸准备超时 本次没有播放成功转场'='startup-preparation-timeout'
        '启动资源保护已触发 本次停止动画并保留 Codex'='startup-resource-protection'
        '转场前客户端或壁纸状态发生变化 本次没有播放成功转场'='handoff-state-changed'
        '启动动画时长无效。'='startup-media-invalid'
        '视频解码器未能打开启动动画。'='startup-media-open-timeout'
        '启动动画播放停止超过 5 秒。'='startup-media-stalled'
        '启动动画超出预期时长。'='startup-media-duration-exceeded'
    }
    foreach($key in $failures.Keys){if($failure -ceq $key){return [pscustomobject]@{utc=$Utc;category=$failures[$key];reason=$failures[$key]}}}
    if($livingFailure){return [pscustomobject]@{utc=$Utc;category='living-preparation-failed';reason='unknown'}}
    return $null
}

function Convert-FixLogEvent([string]$Line,[string]$Relative) {
    if($Line.Length -gt 4096){return $null}
    $match=[regex]::Match($Line.TrimStart([char]0xfeff),'\A(?<utc>\d{4}-\d{2}-\d{2}T\d{2}:\d{2}:\d{2}(?:\.\d{1,7})?Z) (?<body>.*)\z')
    if(-not $match.Success){return $null}
    $utc=[datetime]::MinValue
    if(-not [datetime]::TryParse($match.Groups['utc'].Value,[Globalization.CultureInfo]::InvariantCulture,[Globalization.DateTimeStyles]::RoundtripKind,[ref]$utc)){return $null}
    $stamp=$utc.ToUniversalTime().ToString('o');$body=$match.Groups['body'].Value
    if($Relative -ceq 'diagnostics/portable-launch.log'){
        $codes=@('PUBLIC_ENTRY_BUSY','PUBLIC_PREPARATION_TIMEOUT','PUBLIC_LAUNCH_FAILED','PUBLIC_CHILD_TIMEOUT','PUBLIC_COMPONENT_OR_SIGNATURE_REJECTED','PUBLIC_PREPARATION_FAILED','PUBLIC_UNSUPPORTED_WINDOWS','PUBLIC_CODE_INTEGRITY_FAILED','PUBLIC_MEDIA_INCOMPLETE','PUBLIC_LAUNCHER_CONFIG_INVALID','PUBLIC_LAUNCHER_ASSET_INVALID','PUBLIC_OFFICIAL_CODEX_NOT_INSTALLED','PUBLIC_PACKAGE_ENUMERATION_FAILED','PUBLIC_SUPPORTED_CODEX_EXECUTABLE_NOT_FOUND','PUBLIC_OFFICIAL_DISCOVERY_FAILED','PUBLIC_OFFICIAL_IDENTITY_REJECTED','PUBLIC_OFFICIAL_RUNTIME_UNSUPPORTED')
        if($body -cin $codes){return [pscustomobject]@{utc=$stamp;category='public-entry';publicCode=$body}}
        $exit=[regex]::Match($body,'\APUBLIC_CHILD_EXIT_(?<code>-?\d{1,10})\z');$number=0
        if($exit.Success -and [int]::TryParse($exit.Groups['code'].Value,[ref]$number)){return [pscustomobject]@{utc=$stamp;category='public-entry';publicCode='PUBLIC_CHILD_EXIT';exitCode=$number}}
        return $null
    }
    if($Relative -ceq 'output/wallpaper-readiness.log' -or $Relative -ceq 'output/wallpaper-readiness.log.1'){
        $snapshotHeader=$body -cmatch '\Aphase=10 snapshotKind=[012] ordinal=[1-8] elapsedMs=\d{1,11} sampleAgeMs='
        $rotationHeader=$body -cmatch '\Aphase=35 elapsedMs=\d{1,11} pid=\d{1,10} trace=[a-f0-9]{32} activeClip='
        $normalHeader=$body -cmatch '\Aphase=\d{1,2} elapsedMs=\d{1,11} code=\d{1,5}(?: |$)'
        if(-not ($snapshotHeader -or $rotationHeader -or $normalHeader)){return $null}
        $limits=[ordered]@{phase=63;elapsedMs=31536000000;code=65535;reason=65535;commandKind=2;sourcePhase=63;observationKind=3;requestBudgetMs=30000;timeoutStage=3;bytesReceived=1099511627776;framesReceived=1099511627776;matchedResponses=1099511627776;lateResponses=1099511627776;guardMask=127;snapshotKind=2}
        $fields=[ordered]@{utc=$stamp;category='host-phase'}
        foreach($key in $limits.Keys){
            if($body -cmatch ('(?:^|[\s;])'+[regex]::Escape($key)+'=')){
                $minimum=if($key -ceq 'observationKind'){1}else{0}
                $fields[$key]=Get-FixEventNumber $body $key $limits[$key] $minimum
            }
        }
        if($fields.Contains('phase') -and $fields.phase -ge 0 -and $fields.Contains('elapsedMs') -and $fields.elapsedMs -ge 0){
            if($rotationHeader){
                $fields.activeClip=Get-FixEventEnum $body 'activeClip' @('ambient','particles')
                $fields.completedCycles=Get-FixEventNumber $body 'completedCycles' 2147483647
                foreach($key in @('alternateAvailable','alternateFailed')){$fields[$key]=Get-FixEventNumber $body $key 1}
            }
            if($snapshotHeader){
                $fields.ordinal=Get-FixEventNumber $body 'ordinal' 8 1;$fields.sampleAgeMs=Get-FixEventNumber $body 'sampleAgeMs' 31536000000
                Add-FixSnapshotFields $fields $body
            }
            if($fields.phase -eq 38){
                $fields.layoutReason=Get-FixEventEnum $body 'layoutReason' @('not-checked','root-missing','main-count','main-token','workspace-shape','shell-shape','composer-ambiguous','frame-count','frame-token','frame-row','frame-shape','ready')
                $fields.layoutSurfaceKind=Get-FixEventEnum $body 'layoutSurfaceKind' @('none','main','frame')
                # Keep absent, malformed, duplicated and unknown values explicitly unknown.
                $snapshotLimits=[ordered]@{rootReady=1;bodyReady=1;layoutSupported=1;documentHidden=1;attached=1;mediaReady=1;prepared=1;disposed=1;firstFrameConfirmed=1;generationMatches=1;layoutWindowElectron=1;layoutMainCount=129;layoutActiveMainCount=129;layoutFrameCount=129;layoutActiveFrameCount=129;layoutWorkspaceRowCount=129}
                foreach($key in $snapshotLimits.Keys){
                    $fields[$key]=Get-FixEventNumber $body $key $snapshotLimits[$key]
                }
            }
            if($fields.phase -eq 39 -and $normalHeader -and $fields.code -eq 0){Add-FixSnapshotFields $fields $body -Runtime}
            if($fields.phase -eq 40 -and $normalHeader -and $fields.code -eq 0){
                $qualityLimits=[ordered]@{videoTotalFrames=2147483647;videoDroppedFrames=2147483647;videoCorruptedFrames=2147483647;videoWidth=16384;videoHeight=16384;mediaGeneration=2147483647;presentedFrames=2147483647}
                foreach($key in $qualityLimits.Keys){$fields[$key]=Get-FixEventNumber $body $key $qualityLimits[$key]}
            }
            if($fields.phase -eq 41 -and $normalHeader){
                # Failure-only request telemetry. Preserve ambiguous or absent
                # values as unknown; never export command text or identifiers.
                $fields.operationKind=Get-FixEventNumber $body 'operationKind' 4 1
                $fields.requestElapsedMs=Get-FixEventNumber $body 'requestElapsedMs' 31536000000 0
                $fields.requestBudgetMs=Get-FixEventNumber $body 'requestBudgetMs' 750 1
                $fields.timeoutStage=Get-FixEventNumber $body 'timeoutStage' 3 0
            }
            if($fields.phase -eq 43 -and $normalHeader -and $fields.code -eq 0){
                # Request/pressure evidence is not proof of resumed playback.
                $fields.pressureState=Get-FixEventEnum $body 'pressureState' @('suspended','resume-requested')
                foreach($key in @('availableCommitBytes','totalCommitBytes')){$fields[$key]=Get-FixEventNumber $body $key 281474976710656}
            }
            if($fields.phase -eq 45 -and $normalHeader -and $fields.code -eq 0){
                $fields.layoutReason=Get-FixEventEnum $body 'layoutReason' @('not-checked','root-missing','main-count','main-token','workspace-shape','shell-shape','composer-ambiguous','frame-count','frame-token','frame-row','frame-shape','ready')
                $fields.layoutSurfaceKind=Get-FixEventEnum $body 'layoutSurfaceKind' @('none','main','frame')
                $fields.sourcePhase=Get-FixEventNumber $body 'sourcePhase' 63
                foreach($key in @('layoutAuditFrameCount','layoutAuditActiveFrameCount','layoutAuditAnyMainCount','layoutAuditActiveAnyMainCount','layoutAuditPageSurfaceFrameCount','layoutAuditDirectRowCount','layoutAuditDecorationCount','layoutAuditComposerCount')){$fields[$key]=Get-FixEventNumber $body $key 129}
                foreach($key in @('layoutAuditLayoutTokenValid','layoutAuditPageLayoutTokenValid','layoutAuditFrameHashMatches','layoutAuditPageSurfaceFlag','layoutAuditDecorationValid','layoutAuditComposerAmbiguous')){$fields[$key]=Get-FixEventNumber $body $key 1}
            }
            if($fields.phase -eq 46 -and $normalHeader -and $fields.code -eq 0){
                $fields.sourcePhase=Get-FixEventNumber $body 'sourcePhase' 63
                $fields.lossKind=Get-FixEventEnum $body 'lossKind' @('transport-fault','target-detached','target-crashed','inspector-detached','unknown')
                $fields.transportFaultCode=Get-FixEventNumber $body 'transportFaultCode' 99
                $fields.processExited=Get-FixEventNumber $body 'processExited' 1
            }
            if($fields.phase -eq 47 -and $normalHeader -and $fields.code -eq 0){
                $fields.processExited=Get-FixEventNumber $body 'processExited' 1
                $fields.exitCodeKnown=Get-FixEventNumber $body 'exitCodeKnown' 1
                $fields.processExitCode=Get-FixEventNumber $body 'processExitCode' 4294967295
            }
            if($fields.phase -eq 49 -and $normalHeader -and $fields.code -ge 1 -and $fields.code -le 8){
                $fields.releaseReason=Get-FixEventNumber $body 'releaseReason' 6
                $fields.eligibleHidden=Get-FixEventNumber $body 'eligibleHidden' 129
                $fields.visible=Get-FixEventNumber $body 'visible' 129
                $fields.markerOwned=Get-FixEventNumber $body 'markerOwned' 1
                $fields.observationComplete=Get-FixEventNumber $body 'observationComplete' 1
            }
            return [pscustomobject]$fields
        }
        return $null
    }
    if($Relative -ceq 'output/handoff-latest.log'){
        return Convert-FixHandoff $body $stamp
    }
    # Only fixed exception identifiers are retained; message and stack are discarded.
    $errors=[ordered]@{'System.IO.FileNotFoundException: '='file-not-found';'System.IO.DirectoryNotFoundException: '='directory-not-found';'System.UnauthorizedAccessException: '='access-denied';'System.BadImageFormatException: '='bad-image-format';'System.OutOfMemoryException: '='out-of-memory';'System.TimeoutException: '='timeout';'Missing startup film; '='startup-media-missing';'Reduced-motion activation failed: '='reduced-motion-error';'Reduced-motion scene could not load; '='reduced-motion-error'}
    foreach($prefix in $errors.Keys){if($body.StartsWith($prefix,[StringComparison]::Ordinal)){return [pscustomobject]@{utc=$stamp;category=$errors[$prefix]}}}
    return $null
}

function Get-FixHostRunOrdinal([string]$Line,[Collections.IDictionary]$Runs) {
    # Correlate only inside this report. Never export the source trace, a hash
    # of it, PID, paths or a machine-stable identifier. Bounded overflow is unknown.
    if($null -eq $Runs){return -1}
    $trace=Get-FixEventToken $Line 'trace'
    if($null -eq $trace -or $trace -cnotmatch '\A[a-f0-9]{32}\z'){return -1}
    if($Runs.Contains($trace)){if(Test-FixInteger $Runs[$trace] 1 32){return [int]$Runs[$trace]};return -1}
    if($Runs.Count -ge 32){return -1}
    $ordinal=$Runs.Count+1;$Runs[$trace]=$ordinal;return $ordinal
}
function Read-FixLogTail([string]$Root,[string]$Relative,[Collections.IDictionary]$HostRuns=$null) {
    $result=[ordered]@{log=$Relative;status='missing';bytesRead=0;tailTruncated=$false;recordLimitReached=$false;discardedRecordCount=0;unrecognizedLineCount=0;records=@()}
    try {
        $path=Join-FixPath $Root $Relative
        if(-not (Test-Path -LiteralPath $path -PathType Leaf)){return [pscustomobject]$result}
        [void](Assert-FixPath $path -File)
        $stream=[IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,([IO.FileShare]::ReadWrite -bor [IO.FileShare]::Delete))
        try {
            $count=[int][Math]::Min(262144,$stream.Length);$result.tailTruncated=$stream.Length -gt $count
            if($result.tailTruncated){[void]$stream.Seek(-$count,[IO.SeekOrigin]::End)}
            $buffer=New-Object byte[] $count;$used=0
            while($used -lt $count){$n=$stream.Read($buffer,$used,$count-$used);if($n -eq 0){break};$used+=$n}
        } finally {$stream.Dispose()}
        $result.bytesRead=$used;$result.status='read'
        $text=[Text.Encoding]::UTF8.GetString($buffer,0,$used)
        if($result.tailTruncated){$start=$text.IndexOf("`n");$text=if($start -ge 0){$text.Substring($start+1)}else{''}}
        $records=[Collections.Generic.List[object]]::new()
        foreach($line in ($text -split '\r?\n')) {
            $entry=Convert-FixLogEvent $line $Relative
            if($null -ne $entry){
                if($Relative -cin @('output/wallpaper-readiness.log','output/wallpaper-readiness.log.1')){
                    $entry|Add-Member -NotePropertyName hostRunOrdinal -NotePropertyValue (Get-FixHostRunOrdinal $line $HostRuns)
                }
                $records.Add($entry)
                if($records.Count -gt 512){$records.RemoveAt(0);$result.recordLimitReached=$true;$result.discardedRecordCount++}
            }elseif(-not [string]::IsNullOrWhiteSpace($line)){$result.unrecognizedLineCount++}
        }
        $result.records=@($records.ToArray())
    } catch {$result.status='unavailable';$result.records=@()}
    return [pscustomobject]$result
}

function Read-PortableReportManifest([string]$Root) {
    $path=Assert-FixPath (Join-FixPath $Root 'release-manifest.json') -File
    # Parse and hash the same bounded snapshot; never hash an arbitrarily large file.
    $stream=[IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
    try {
        if($stream.Length -lt 2 -or $stream.Length -gt 262144){Stop-Fix 'MANIFEST-SIZE'}
        $bytes=New-Object byte[] ([int]$stream.Length);$used=0
        while($used -lt $bytes.Length){$count=$stream.Read($bytes,$used,$bytes.Length-$used);if($count -eq 0){Stop-Fix 'MANIFEST-READ'};$used+=$count}
    } finally {$stream.Dispose()}
    $text=[Text.UTF8Encoding]::new($false,$true).GetString($bytes).TrimStart([char]0xfeff)
    try {$manifest=ConvertFrom-Json -InputObject $text}catch{Stop-Fix 'MANIFEST-JSON'}
    if($null -eq $manifest -or $manifest -is [array] -or $manifest -is [string]){Stop-Fix 'RELEASE-SCHEMA'}
    foreach($key in @('schemaVersion','product','files')){if($null -eq $manifest.PSObject.Properties[$key]){Stop-Fix 'RELEASE-SCHEMA'}}
    if(-not (Test-FixInteger $manifest.schemaVersion 1 1) -or $manifest.product -cne 'CodexDragonPublic' -or $manifest.files -isnot [array]){Stop-Fix 'RELEASE-SCHEMA'}
    $count=@($manifest.files).Count
    Assert-FixRecords $Root $manifest.files $count
    $sha=[Security.Cryptography.SHA256]::Create()
    try {$hash=[BitConverter]::ToString($sha.ComputeHash($bytes)).Replace('-','')}finally{$sha.Dispose()}
    return [pscustomobject]@{sha256=$hash;declaredFileCount=$count;files=$manifest.files}
}
function Get-PortableMediaPresence([string]$Root,$Records) {
    # A quick size/presence check, explicitly not a hash or decoder verification.
    # Only known package media roles may be exported, never caller-selected paths.
    $roles=[ordered]@{
        startup='output/codex-startup-2k-144fps-crf16.mp4'
        intro='output/versions/startup-living-v6/startup-intro.mp4'
        idle='output/versions/startup-living-v6/startup-idle.mp4'
        finale='output/versions/startup-living-v6/startup-finale.mp4'
        ambient='output/versions/ai-refined-v4/dragon-wallpaper-ambient-2k-144fps.mp4'
        particles='output/versions/particle-v33/dragon-wallpaper-particles-1440p48.mp4'
    }
    $result=@()
    foreach($role in $roles.Keys){
        $entry=[ordered]@{role=$role;state='unavailable';bytes=-1L;declaredBytes=-1L;sizeMatches=-1;hashVerified=$false;decodingVerified=$false}
        $declared=@($Records|Where-Object {$_.path -ceq $roles[$role]})
        if($declared.Count -eq 1){$entry.declaredBytes=[long]$declared[0].bytes}
        try {
            $path=Join-FixPath $Root $roles[$role]
            [void](Assert-FixPath $path)
            if([IO.File]::Exists($path)){
                $entry.state='present';$entry.bytes=([IO.FileInfo]::new($path)).Length
                if($entry.declaredBytes -ge 0){$entry.sizeMatches=[int]($entry.bytes -eq $entry.declaredBytes)}
            }else{$entry.state='missing'}
        }catch{}
        $result+= [pscustomobject]$entry
    }
    return ,$result
}
function Get-PortableIdentityPolicy([string]$Root) {
    # Generated runtime configuration is deliberately not a release member.
    # Export only its fixed policy shape; no package names, hashes, paths or
    # unknown JSON values can enter the shareable report.
    $result=[ordered]@{state='unavailable';policyVersion=-1;identityMode='unknown';familyMatches=-1;legacyPinsPresent=-1;identityVerified=$false}
    $stream=$null
    try {
        $path=Join-FixPath $Root 'wallpaper-host-config.json'
        if(-not [IO.File]::Exists($path)){$result.state='missing';return [pscustomobject]$result}
        $stream=[IO.File]::Open($path,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read)
        if($stream.Length -lt 2 -or $stream.Length -gt 16384){$result.state='invalid';return [pscustomobject]$result}
        $bytes=New-Object byte[] ([int]$stream.Length);$used=0
        while($used -lt $bytes.Length){$read=$stream.Read($bytes,$used,$bytes.Length-$used);if($read -eq 0){throw 'FCP-POLICY-SNAPSHOT'};$used+=$read}
        $text=[Text.UTF8Encoding]::new($false,$true).GetString($bytes).TrimStart([char]0xfeff)
        if(-not $text.TrimStart().StartsWith('{',[StringComparison]::Ordinal)){$result.state='invalid';return [pscustomobject]$result}
        $value=ConvertFrom-Json -InputObject $text
        if($value -isnot [pscustomobject]){$result.state='invalid';return [pscustomobject]$result}
        foreach($key in @('version','officialIdentityMode','officialFamilyName','packageFullName','executableSha256')){
            $expected=[int]($null -ne $value.PSObject.Properties[$key])
            if([regex]::Matches($text,'"'+[regex]::Escape($key)+'"\s*:').Count -ne $expected){$result.state='invalid';return [pscustomobject]$result}
        }
        $version=$value.PSObject.Properties['version'];$mode=$value.PSObject.Properties['officialIdentityMode'];$family=$value.PSObject.Properties['officialFamilyName']
        if($null -ne $version -and (Test-FixInteger $version.Value 1 2)){$result.policyVersion=[int]$version.Value}
        if($null -ne $mode -and $mode.Value -is [string] -and $mode.Value -ceq 'registered-signed'){$result.identityMode='registered-signed'}
        if($null -ne $family -and $family.Value -is [string]){$result.familyMatches=[int]($family.Value -ceq 'OpenAI.Codex_2p2nqsd0c76g0')}
        $result.legacyPinsPresent=[int]($null -ne $value.PSObject.Properties['packageFullName'] -or $null -ne $value.PSObject.Properties['executableSha256'])
        $result.state='read'
    }catch{$result.state='unavailable'}finally{if($stream){$stream.Dispose()}}
    return [pscustomobject]$result
}
function Get-PortableEnvironmentEvidence {
    try {
        $module=Join-FixPath $script:PortableReportToolsRoot 'EnvironmentDiagnostics.ps1'
        [void](Assert-FixPath $module -File)
        . $module
        return Get-CodexEnvironmentEvidence
    }catch{return [pscustomobject]@{schemaVersion=1;kind='CodexEnvironmentEvidence';state='unavailable'}}
}
function Get-PortableDiagnosticReport([string]$ReleaseRoot) {
    $root=Assert-FixPath $ReleaseRoot -Directory
    $manifest=Read-PortableReportManifest $root
    $hostRuns=@{};$logs=@();foreach($name in @('diagnostics/portable-launch.log','output/wallpaper-readiness.log.1','output/wallpaper-readiness.log','output/handoff-latest.log','output/launcher-error.txt','output/launcher-media-error.txt')){$logs+=Read-FixLogTail $root $name $hostRuns}
    $compatibility=[pscustomobject]@{schemaVersion=1;kind='CodexCompatibilityEvidence';registrationState='not-collected';transportSupportVerified=$false}
    try {
        $probe=Join-FixPath $script:PortableReportToolsRoot 'CompatibilityProbe.ps1'
        if(Test-Path -LiteralPath $probe -PathType Leaf){
            [void](Assert-FixPath $probe -File)
            . $probe
            $compatibility=Get-CodexCompatibilityInventory
        }
    }catch{$compatibility=[pscustomobject]@{schemaVersion=1;kind='CodexCompatibilityEvidence';registrationState='unavailable';transportSupportVerified=$false}}
    return [pscustomobject][ordered]@{
        schemaVersion=1;kind='CodexDragonStartupFixReport';collectedUtc=[DateTime]::UtcNow.ToString('o')
        targetManifestSha256=$manifest.sha256;declaredFileCount=$manifest.declaredFileCount;verifiedFileCountAtPreparation=-1
        logByteLimit=262144;logRecordLimit=512;rawLogsIncluded=$false;logs=$logs
        compatibility=$compatibility;environment=(Get-PortableEnvironmentEvidence);mediaPresence=(Get-PortableMediaPresence $root $manifest.files);officialPolicy=(Get-PortableIdentityPolicy $root)
    }
}
function Write-PortableDiagnosticReport([string]$ReleaseRoot) {
    $root=Assert-FixPath $ReleaseRoot -Directory
    $report=Get-PortableDiagnosticReport $root
    $folder=Join-FixPath $root 'CodexDiagnosticReports'
    [void][IO.Directory]::CreateDirectory($folder);[void](Assert-FixPath $folder -Directory)
    $path=Join-FixPath $folder ('report-'+[DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N').Substring(0,8)+'.zip')
    Add-Type -AssemblyName System.IO.Compression
    $stream=[IO.File]::Open($path,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
    try {
        $zip=[IO.Compression.ZipArchive]::new($stream,[IO.Compression.ZipArchiveMode]::Create,$true)
        try {
            $entry=$zip.CreateEntry('numeric-report.json');$entryStream=$entry.Open()
            try {$bytes=[Text.UTF8Encoding]::new($false).GetBytes(($report|ConvertTo-Json -Depth 8));$entryStream.Write($bytes,0,$bytes.Length)}finally{$entryStream.Dispose()}
        } finally {$zip.Dispose()}
    } finally {$stream.Dispose()}
    return $path
}
