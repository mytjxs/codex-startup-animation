[CmdletBinding()]
param([string]$ToolsRoot,[Parameter(Mandatory=$true)][string]$WorkRoot)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
if(-not $PSBoundParameters.ContainsKey('ToolsRoot')){$ToolsRoot=Join-Path (Split-Path -Parent (Split-Path -Parent $PSCommandPath)) 'packaging'}
$ToolsRoot=[IO.Path]::GetFullPath($ToolsRoot)
. (Join-Path $ToolsRoot 'DiagnosticReport.Common.ps1')
Assert-FixPowerShell
[void][IO.Directory]::CreateDirectory($WorkRoot)
$WorkRoot=Assert-FixPath $WorkRoot -Directory
$script:checks=0
function Check([bool]$Value,[string]$Name){if(-not $Value){throw ('TEST FAILED: '+$Name)};$script:checks++;Write-Host ('PASS '+$Name)}
function Save([string]$Path,[string]$Value){[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Path));[IO.File]::WriteAllText($Path,$Value,[Text.UTF8Encoding]::new($false))}
function Json([string]$Path,$Value){Save $Path ($Value|ConvertTo-Json -Depth 8)}
function Reject([scriptblock]$Action,[string]$Code,[string]$Name){$caught=$false;try{& $Action|Out-Null}catch{$caught=$_.Exception.Message -ceq ('FCP-'+$Code)};Check $caught $Name}
function ReadReport([string]$Path){$archive=[IO.Compression.ZipFile]::OpenRead($Path);try{Check ($archive.Entries.Count -eq 1 -and $archive.Entries[0].FullName -ceq 'numeric-report.json') 'one sanitized report member';$reader=[IO.StreamReader]::new($archive.Entries[0].Open());try{return $reader.ReadToEnd()}finally{$reader.Dispose()}}finally{$archive.Dispose()}}
Add-Type -AssemblyName System.IO.Compression.FileSystem
$root=Join-Path $WorkRoot 'Synthetic Local Package'
if(Test-Path -LiteralPath $root){throw 'Use a fresh work directory; existing fixture data is never overwritten.'}
[void][IO.Directory]::CreateDirectory($root)
$roles=[ordered]@{
    startup='output/codex-startup-2k-144fps-crf16.mp4'
    intro='output/versions/startup-living-v6/startup-intro.mp4'
    idle='output/versions/startup-living-v6/startup-idle.mp4'
    finale='output/versions/startup-living-v6/startup-finale.mp4'
    ambient='output/versions/ai-refined-v4/dragon-wallpaper-ambient-2k-144fps.mp4'
    particles='output/versions/particle-v33/dragon-wallpaper-particles-1440p48.mp4'
}
$records=@();$contents=@{}
foreach($role in $roles.Keys){$path=Join-Path $root $roles[$role];$contents[$role]='Synthetic fixture '+$role;Save $path $contents[$role];$records+=[pscustomobject]@{path=$roles[$role];bytes=([IO.FileInfo]::new($path)).Length;sha256=(Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash}}
foreach($name in @('Collect-DiagnosticReport.ps1','DiagnosticReport.Common.ps1','CompatibilityProbe.ps1')){Copy-Item -LiteralPath (Join-Path $ToolsRoot $name) -Destination (Join-Path $root $name)}
$manifest=[ordered]@{schemaVersion=1;product='CodexDragonPublic';files=$records}
Json (Join-Path $root 'release-manifest.json') $manifest
Save (Join-Path $root 'output/wallpaper-readiness.log') (@(
    '2026-10-22T10:00:00Z phase=38 elapsedMs=1200 code=0 sourcePhase=6 layoutReason=composer-ambiguous layoutSupported=0 ignored=C:\Users\PRIVATE\SECRET',
    '2026-10-22T10:00:01Z phase=38 elapsedMs=1300 code=0 sourcePhase=6 layoutReason=C:\Users\PRIVATE\SECRET apiKey=SECRET',
    '2026-10-22T10:00:02Z phase=38 elapsedMs=1400 code=0 sourcePhase=6 layoutReason=ready layoutReason=PRIVATE',
    '2026-10-22T10:00:03Z phase=39 elapsedMs=1500 code=0 layoutReason=ready rendererCode=playing PRIVATE=SECRET'
) -join "`n")
Save (Join-Path $root 'output/private.log') 'PRIVATE SECRET apiKey=SECRET'
$before=@{};Get-ChildItem -LiteralPath $root -Recurse -File|ForEach-Object {$before[$_.FullName]=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
$ps51=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
$clock=[Diagnostics.Stopwatch]::StartNew()
$run=@(& $ps51 -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'Collect-DiagnosticReport.ps1') 2>&1)
Check ($LASTEXITCODE -eq 0) 'complete collector wrapper exits successfully'
$runMs=$clock.ElapsedMilliseconds
$reports=@(Get-ChildItem -LiteralPath (Join-Path $root 'CodexDiagnosticReports') -File -Filter '*.zip')
Check ($reports.Count -eq 1) 'wrapper creates exactly one report ZIP'
$raw=ReadReport $reports[0].FullName;$report=$raw|ConvertFrom-Json
Check ($report.mediaPresence.Count -eq 6) 'six recognized media roles reported'
foreach($role in $roles.Keys){$item=@($report.mediaPresence|Where-Object {$_.role -ceq $role});Check ($item.Count -eq 1 -and $item[0].state -ceq 'present' -and $item[0].sizeMatches -eq 1 -and -not $item[0].hashVerified -and -not $item[0].decodingVerified) ('presence is not hash or decoder proof: '+$role)}
Check ($report.compatibility.kind -ceq 'CodexCompatibilityEvidence' -and $report.compatibility.schemaVersion -eq 1 -and -not $report.compatibility.transportSupportVerified) 'runtime inventory does not claim wallpaper transport support'
Check ($report.compatibility.registrationState -cin @('ok','capped','unavailable')) 'collector actually integrates optional compatibility inventory'
Check ($raw -cnotmatch 'PRIVATE|SECRET|apiKey|OpenAI\.Codex_|FileName|CommandLine|windowTitle' -and -not $raw.Contains($root) -and -not $raw.Contains($ToolsRoot)) 'report excludes fixture secrets package names paths and command lines'
Check (-not $report.rawLogsIncluded -and $report.verifiedFileCountAtPreparation -eq -1) 'report does not invent integrity verification'
$events=@($report.logs|Where-Object {$_.log -ceq 'output/wallpaper-readiness.log'})[0].records
Check ($events.Count -eq 4 -and $events[0].layoutReason -ceq 'composer-ambiguous' -and $events[1].layoutReason -ceq 'unknown' -and $events[2].layoutReason -ceq 'unknown' -and $events[3].layoutReason -ceq 'ready') 'known unknown duplicate and runtime layout reasons'
foreach($path in $before.Keys){Check ((Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ceq $before[$path]) 'collector leaves existing input bytes unchanged'}
Check ((Get-ChildItem -LiteralPath $root -Recurse -File).Count -eq $before.Count+1) 'collector adds only report ZIP'
foreach($reason in @('not-checked','root-missing','main-count','main-token','workspace-shape','shell-shape','composer-ambiguous','ready')){
    $event=Convert-FixLogEvent ('2026-10-22T10:00:00Z phase=38 elapsedMs=0 code=0 layoutReason='+$reason) 'output/wallpaper-readiness.log'
    Check ($event.layoutReason -ceq $reason) ('layout reason round trip: '+$reason)
}
$ambient=Join-FixPath $root $roles.ambient
if(-not $ambient.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Fixture path escaped test root'}
[IO.File]::Delete($ambient)
$missing=Get-PortableDiagnosticReport $root
$media=@($missing.mediaPresence|Where-Object {$_.role -ceq 'ambient'})[0]
Check ($media.state -ceq 'missing' -and $media.bytes -eq -1 -and $media.sizeMatches -eq -1 -and -not $media.hashVerified -and -not $media.decodingVerified) 'missing media stays explicit and unverified'
Save $ambient $contents.ambient
$particle=Join-FixPath $root $roles.particles
Save $particle ($contents.particles+' extra')
$wrong=Get-PortableDiagnosticReport $root
$media=@($wrong.mediaPresence|Where-Object {$_.role -ceq 'particles'})[0]
Check ($media.state -ceq 'present' -and $media.sizeMatches -eq 0 -and -not $media.hashVerified) 'wrong-size media does not pass presence validation'
Save $particle $contents.particles
$badRoot=Join-Path $WorkRoot 'Malformed Manifest';[void][IO.Directory]::CreateDirectory($badRoot)
Save (Join-Path $badRoot 'release-manifest.json') '{x'
Reject {Get-PortableDiagnosticReport $badRoot} 'MANIFEST-JSON' 'malformed manifest rejected before collection'
Json (Join-Path $badRoot 'release-manifest.json') ([ordered]@{schemaVersion=1;product='CodexDragonPublic';files=@([pscustomobject]@{path='../PRIVATE';bytes=1;sha256=('A'*64)})})
Reject {Get-PortableDiagnosticReport $badRoot} 'MEMBER-PATH' 'manifest traversal rejected'
$linkRoot=Join-Path $WorkRoot 'Linked Media';[void][IO.Directory]::CreateDirectory($linkRoot)
Json (Join-Path $linkRoot 'release-manifest.json') $manifest
$link=Join-Path $linkRoot 'output';$linkMade=$false
try{New-Item -ItemType Junction -Path $link -Target (Join-Path $root 'output') -ErrorAction Stop|Out-Null;$linkMade=$true}catch{}
if($linkMade){
    Reject {Get-PortableDiagnosticReport $linkRoot} 'REPARSE-PATH' 'report rejects unsafe media ancestor'
    $linked=Get-PortableMediaPresence $linkRoot $records
    Check (@($linked|Where-Object {$_.state -cne 'unavailable' -or $_.bytes -ne -1 -or $_.hashVerified -or $_.decodingVerified}).Count -eq 0) 'presence probe never follows a linked media ancestor'
}else{Write-Output 'SKIP junction creation unavailable; no elevation requested'}
$receipt=[ordered]@{passed=$true;checks=$checks;collectorRuntimeMs=$runMs;scope='Synthetic media and log fixtures plus read-only registered-package inventory. No application launched.'}
Json (Join-Path $WorkRoot 'results.json') $receipt
Write-Output ('ALL PASS: compatibility report checks='+$checks+'; collectorRuntimeMs='+$runMs)
