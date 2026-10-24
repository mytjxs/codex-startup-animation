[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$WorkRoot)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
if($PSVersionTable.PSVersion.Major -ne 5){throw 'Requires Windows PowerShell 5.1'}
$source=Split-Path -Parent $PSScriptRoot
. (Join-Path $source 'packaging/DiagnosticReport.Common.ps1')
. (Join-Path $source 'packaging/DiagnosticAnalysis.ps1')
if(Test-Path -LiteralPath $WorkRoot){throw 'Fresh isolated output required'}
[void][IO.Directory]::CreateDirectory($WorkRoot)
$checks=0
function Check([bool]$Value,[string]$Name){if(-not $Value){throw ('FAIL '+$Name)};$script:checks++;Write-Output ('PASS '+$Name)}
$config=Join-Path $WorkRoot 'wallpaper-host-config.json'
function Policy([string]$Json){[IO.File]::WriteAllText($config,$Json,[Text.UTF8Encoding]::new($false));return Get-PortableIdentityPolicy $WorkRoot}
function NoPrivate($Value){return (($Value|ConvertTo-Json -Depth 8 -Compress) -notmatch 'PRIVATE|PRIVATE_PATH|PRIVATE_ACCOUNT|[A-Z]:\\|packageFullName|executableSha256|pid|trace')}
function Explain($Policy,$Records){
    $report=[pscustomobject]@{officialPolicy=$Policy;logs=@([pscustomobject]@{log='output/wallpaper-readiness.log';records=@($Records)})}
    return (Get-CodexOfficialAdaptationAnalysis $report) -join "`n"
}
$policy=Get-PortableIdentityPolicy $WorkRoot
Check ($policy.state -ceq 'missing' -and $policy.policyVersion -eq -1 -and -not $policy.identityVerified) 'missing generated config is not identity failure or success'
$good='{"version":2,"officialFamilyName":"OpenAI.Codex_2p2nqsd0c76g0","officialIdentityMode":"registered-signed","extra":"PRIVATE_PATH PRIVATE_ACCOUNT"}'
$policy=Policy $good
Check ($policy.state -ceq 'read' -and $policy.policyVersion -eq 2 -and $policy.identityMode -ceq 'registered-signed' -and $policy.familyMatches -eq 1 -and $policy.legacyPinsPresent -eq 0 -and -not $policy.identityVerified) 'automatic policy projected without claiming trust or playback'
Check (NoPrivate $policy) 'unknown config values never exported'
Check ((Explain $policy @()).Contains('没有固定官方版本号或 EXE 哈希')) 'automatic policy explanation'
foreach($key in @('packageFullName','executableSha256')){
    $legacy=Policy ($good.Substring(0,$good.Length-1)+',"'+$key+'":"PRIVATE"}')
    Check ($legacy.legacyPinsPresent -eq 1 -and (NoPrivate $legacy)) ('old field presence retained without value: '+$key)
    Check ((Explain $legacy @()).Contains('仍含旧版官方客户端固定版本或哈希字段')) ('old field explanation: '+$key)
}
$legacy=Policy '{"version":1,"packageFullName":"PRIVATE","executableSha256":"PRIVATE"}'
Check ($legacy.policyVersion -eq 1 -and $legacy.identityMode -ceq 'unknown' -and $legacy.familyMatches -eq -1 -and $legacy.legacyPinsPresent -eq 1) 'legacy schema is not automatic policy'
foreach($text in @('{"version":2,"version":1}','{"ver\u0073ion":2}','{"version":2,"child":{"version":2}}','[]','null','"PRIVATE"',('x'*16385))){
    $invalid=Policy $text
    Check ($invalid.state -ceq 'invalid' -and $invalid.policyVersion -eq -1 -and -not $invalid.identityVerified) 'ambiguous or unsupported config not classified as automatic'
}
$invalid=Policy '{PRIVATE'
Check ($invalid.state -ceq 'unavailable' -and (NoPrivate $invalid)) 'invalid JSON captured without exception body'
$unknown=Policy '{"version":99,"officialFamilyName":"PRIVATE","officialIdentityMode":"PRIVATE"}'
Check ($unknown.policyVersion -eq -1 -and $unknown.identityMode -ceq 'unknown' -and $unknown.familyMatches -eq 0 -and (NoPrivate $unknown)) 'unknown mode/version/family projected without echo'
[IO.File]::WriteAllText($config,$good,[Text.UTF8Encoding]::new($false))
$held=[IO.File]::Open($config,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
try{$locked=Get-PortableIdentityPolicy $WorkRoot;Check ($locked.state -ceq 'unavailable' -and -not $locked.identityVerified) 'locked generated config yields bounded unknown'}finally{$held.Dispose()}
$stamp='2026-10-22T20:00:00.0000000Z '
$line=$stamp+'Native hidden maximized placement confirmed; originalShow=1; capturedShow=1; currentShow=3; lateMaximized=True; title=PRIVATE pid=778899 path=PRIVATE_PATH'
$event=Convert-FixLogEvent $line 'output/handoff-latest.log'
Check ($event.category -ceq 'native-hidden-maximized-placement' -and $event.originalShow -eq 1 -and $event.capturedShow -eq 1 -and $event.currentShow -eq 3 -and $event.lateMaximized -eq 1) 'late maximized real policy evidence retained'
Check (NoPrivate $event) 'window title/process/path discarded'
Check ($null -eq (Convert-FixLogEvent $line 'output/wallpaper-readiness.log')) 'window evidence rejected in incorrect log'
$event=Convert-FixLogEvent ($line+' currentShow=PRIVATE lateMaximized=PRIVATE') 'output/handoff-latest.log'
Check ($event.currentShow -eq -1 -and $event.lateMaximized -eq -1) 'ambiguous window fields remain unknown'
foreach($code in @('PUBLIC_OFFICIAL_DISCOVERY_FAILED','PUBLIC_OFFICIAL_IDENTITY_REJECTED','PUBLIC_OFFICIAL_RUNTIME_UNSUPPORTED')){
    $event=Convert-FixLogEvent ($stamp+$code) 'diagnostics/portable-launch.log'
    Check ($event.category -ceq 'public-entry' -and $event.publicCode -ceq $code) ('public identity code retained: '+$code)
    Check ($null -eq (Convert-FixLogEvent ($stamp+$code+' PRIVATE') 'diagnostics/portable-launch.log')) 'public code with unexpected payload not echoed'
}
foreach($code in @(1,2,3,10,11,12,13,14,15)){
    $event=Convert-FixLogEvent ($stamp+'phase=44 elapsedMs=123 code='+$code+' pid=778899 trace=PRIVATE path=PRIVATE') 'output/wallpaper-readiness.log'
    Check ($event.phase -eq 44 -and $event.code -eq $code -and (NoPrivate $event)) ('fixed identity result retained without private fields: '+$code)
    $text=Explain $null @($event)
    Check ($text.Contains('官方身份适配记录：') -and -not $text.Contains('PRIVATE')) ('identity result explained: '+$code)
    if($code -eq 15){Check ($text.Contains('不能单独认定是身份解析失败')) 'unclassified startup catch is not misattributed to identity resolver'}
    Check ($null -eq (Get-CodexAnalysisEvent $event)) 'identity evidence never substituted for playback evidence'
}
$early=Convert-FixLogEvent ($stamp+'phase=44 elapsedMs=1 code=2') 'output/wallpaper-readiness.log'
$newStart=Convert-FixLogEvent '2026-10-22T20:00:01.0000000Z phase=20 elapsedMs=0 code=0' 'output/wallpaper-readiness.log'
Check ([string]::IsNullOrEmpty((Explain $null @($early,$newStart)))) 'older identity result not applied across later startup'
$conflict=Convert-FixLogEvent ($stamp+'phase=44 elapsedMs=1 code=14') 'output/wallpaper-readiness.log'
Check ((Explain $null @($early,$conflict)).Contains('记录不一致')) 'same-time conflicting identity evidence not arbitrarily selected'
$unknown=Convert-FixLogEvent ($stamp+'phase=44 elapsedMs=1 code=999') 'output/wallpaper-readiness.log'
Check ((Explain $null @($unknown)).Contains('未知结果')) 'unknown future identity code not interpreted as success'
$duplicate=Convert-FixLogEvent ($stamp+'phase=44 elapsedMs=1 code=2 code=14') 'output/wallpaper-readiness.log'
Check ($duplicate.code -eq -1 -and (Explain $null @($duplicate)).Contains('未知结果')) 'duplicate identity result remains unknown'
[IO.File]::WriteAllText((Join-Path $WorkRoot 'results.json'),(@{passed=$true;checks=$checks;applicationStarted=$false;gpuStress=$false}|ConvertTo-Json),[Text.UTF8Encoding]::new($false))
Write-Output ('ALL PASS: '+$checks+' automatic identity diagnostic checks')
