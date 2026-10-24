[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$WorkRoot)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
. (Join-Path (Split-Path -Parent $PSScriptRoot) 'packaging/DiagnosticReport.Common.ps1')
if($PSVersionTable.PSVersion.Major -ne 5){throw 'Requires Windows PowerShell 5.1'}
$checks=0
function Check([bool]$Value,[string]$Name){if(-not $Value){throw ('FAIL '+$Name)};$script:checks++;Write-Output ('PASS '+$Name)}
$stamp='2026-10-22T12:28:21.9000000Z '
$errorLine=$stamp+'replay-diagnostic operation=find phase=5 elapsedMs=165 reason=5 exceptionType=Win32Exception nativeErrorCode=5 hresult=-2147467259 path=PRIVATE_PATH account=PRIVATE_ACCOUNT'
$event=Convert-FixLogEvent $errorLine 'output/handoff-latest.log'
Check ($null -ne $event) 'read-only access denial retained'
Check ($event.category -ceq 'replay-diagnostic' -and $event.operation -ceq 'find' -and $event.phase -eq 5 -and $event.reason -eq 5 -and $event.nativeErrorCode -eq 5 -and $event.hresult -eq -2147467259) 'access-denial phase and numeric error retained'
$json=$event|ConvertTo-Json -Compress
Check ($json -notmatch 'PRIVATE|path|account|pid|trace') 'private extra fields discarded'
Check ($null -eq (Convert-FixLogEvent $errorLine 'output/wallpaper-readiness.log')) 'wrong log source ignored'
$event=Convert-FixLogEvent ($stamp+'replay-diagnostic operation=attest phase=4 elapsedMs=22 reason=10 exceptionType=None nativeErrorCode=0 hresult=0') 'output/handoff-latest.log'
Check ($event.operation -ceq 'attest' -and $event.reason -eq 10 -and $event.nativeErrorCode -eq 0) 'unregistered pin distinct from access denial'
$event=Convert-FixLogEvent ($errorLine+' phase=PRIVATE reason=PRIVATE nativeErrorCode=PRIVATE hresult=PRIVATE operation=PRIVATE exceptionType=PRIVATE') 'output/handoff-latest.log'
Check ($event.phase -eq -1 -and $event.reason -eq -1 -and $event.nativeErrorCode -eq -1 -and $event.hresult -eq -1 -and $event.operation -ceq 'unknown' -and $event.exceptionType -ceq 'unknown') 'duplicates remain unknown'
$event=Convert-FixLogEvent ($stamp+'replay-diagnostic operation=private phase=17 reason=33 elapsedMs=31536000001 exceptionType=PRIVATE nativeErrorCode=2147483648 hresult=-2147483649') 'output/handoff-latest.log'
Check ($event.operation -ceq 'unknown' -and $event.phase -eq -1 -and $event.reason -eq -1 -and $event.elapsedMs -eq -1 -and $event.exceptionType -ceq 'unknown' -and $event.nativeErrorCode -eq -1 -and $event.hresult -eq -1) 'out-of-range and unknown values rejected'
$event=Convert-FixLogEvent ($stamp+'bridge-diagnostic trace=SECRET pid=1234 operation=discover phase=2 elapsedMs=3 reason=2 outcome=none exceptionType=Win32Exception nativeErrorCode=5 hresult=-2147467259 hostPid=1234 disposition=-1 mediaCode=-1') 'output/handoff-latest.log'
Check ($event.nativeErrorCode -eq 5 -and $event.exceptionType -ceq 'Win32Exception' -and ($event|ConvertTo-Json -Compress) -notmatch 'SECRET|1234|hostPid|trace') 'bridge error retained without process or session identity'
$event=Convert-FixLogEvent ($stamp+'replay-native-candidates phase=1 verifiedWindows=1; visible=1; hidden=0; cloaked=0; eligible=1 title=PRIVATE') 'output/handoff-latest.log'
Check ($event.category -ceq 'replay-native-candidates' -and $event.phase -eq 1 -and $event.eligible -eq 1 -and ($event|ConvertTo-Json -Compress) -notmatch 'PRIVATE|title') 'native counts without titles'
$event=Convert-FixLogEvent ($stamp+'replay-native-candidates phase=4 countsUnavailable=true') 'output/handoff-latest.log'
Check ($event.phase -eq 4 -and $event.eligible -eq -1 -and $event.visible -eq -1) 'missing counts remain unknown'
[void][IO.Directory]::CreateDirectory($WorkRoot)
[IO.File]::WriteAllText((Join-Path $WorkRoot 'results.json'),(@{passed=$true;checks=$checks}|ConvertTo-Json),[Text.UTF8Encoding]::new($false))
Write-Output ('ALL PASS: '+$checks+' replay diagnostic checks')
