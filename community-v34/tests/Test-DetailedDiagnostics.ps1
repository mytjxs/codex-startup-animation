[CmdletBinding()]
param([string]$ToolsRoot,[Parameter(Mandatory=$true)][string]$WorkRoot)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
if(-not $PSBoundParameters.ContainsKey('ToolsRoot')){$ToolsRoot=Join-Path ([IO.Path]::GetDirectoryName([IO.Path]::GetDirectoryName($PSCommandPath))) 'packaging'}
if($PSVersionTable.PSVersion.Major -ne 5 -or $PSVersionTable.PSVersion.Minor -lt 1){throw 'TEST REQUIRES WINDOWS POWERSHELL 5.1'}
$WorkRoot=[IO.Path]::GetFullPath($WorkRoot);[void][IO.Directory]::CreateDirectory($WorkRoot)
$root=Join-Path $WorkRoot 'synthetic-package'
if([IO.Directory]::Exists($root)){throw 'Use a new WorkRoot; existing fixtures are not removed or overwritten'}
[void][IO.Directory]::CreateDirectory($root)
$script:checks=0;$script:scenarios=@()
function Check([bool]$Value,[string]$Name){if(-not $Value){throw ('TEST FAILED: '+$Name)};$script:checks++;Write-Host ('PASS '+$Name)}
function Save([string]$Relative,[string]$Text){$path=Join-Path $root $Relative;[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($path));[IO.File]::WriteAllText($path,$Text,[Text.UTF8Encoding]::new($true))}
function SaveJson([string]$Relative,$Value){Save $Relative ($Value|ConvertTo-Json -Depth 10)}
$helpers=@('PackageTools.ps1','DiagnosticReport.Common.ps1','DiagnosticAnalysis.ps1','CompatibilityProbe.ps1','EnvironmentDiagnostics.ps1')
foreach($file in $helpers){[IO.File]::Copy((Join-Path $ToolsRoot $file),(Join-Path $root $file))}
[void][IO.Directory]::CreateDirectory((Join-Path $root 'Diagnostics'))
foreach($file in @('Run-Diagnostics.ps1','One-Click-Diagnostics.cmd','README.txt')){[IO.File]::Copy((Join-Path $ToolsRoot ('Diagnostics\'+$file)),(Join-Path $root ('Diagnostics\'+$file)))}
# Metadata-only assembly fixture. Reflection.Emit saves a non-runnable class
# library carrying the expected framework attribute. Nothing is launched.
foreach($file in @('Launch.exe','Start-Codex-Dragon.exe','WallpaperControls.exe','WallpaperHost.exe')){
    $assemblyName=[Reflection.AssemblyName]::new('SyntheticMetadata'+[Guid]::NewGuid().ToString('N'))
    $assembly=[AppDomain]::CurrentDomain.DefineDynamicAssembly($assemblyName,[Reflection.Emit.AssemblyBuilderAccess]::RunAndSave,$root)
    $module=$assembly.DefineDynamicModule($assemblyName.Name,$file)
    [void]$module.DefineType('SyntheticMetadata',[Reflection.TypeAttributes]::Public).CreateType()
    $constructor=[Runtime.Versioning.TargetFrameworkAttribute].GetConstructor([type[]]@([string]))
    $attribute=[Reflection.Emit.CustomAttributeBuilder]::new($constructor,[object[]]@('.NETFramework,Version=v4.8'))
    $assembly.SetCustomAttribute($attribute);$assembly.Save($file)
}
foreach($file in @('renderer.js','launcher.config.xml','Install.ps1','Uninstall.ps1','README.txt')){Save $file 'PRIVATE_SENTRY synthetic fixture only, never execute'}
$media=[ordered]@{startup='output/codex-startup-2k-144fps-crf16.mp4';intro='output/versions/startup-living-v6/startup-intro.mp4';idle='output/versions/startup-living-v6/startup-idle.mp4';finale='output/versions/startup-living-v6/startup-finale.mp4';ambient='output/versions/ai-refined-v4/dragon-wallpaper-ambient-2k-144fps.mp4';particles='output/versions/particle-v33/dragon-wallpaper-particles-1440p48.mp4'}
foreach($file in $media.Values){Save $file 'SYNTHETIC_MEDIA_DATA_NOT_A_VIDEO'}
$records=@();Get-ChildItem -LiteralPath $root -Recurse -File|Sort-Object FullName|ForEach-Object {$relative=$_.FullName.Substring($root.Length+1).Replace('\','/');$records+=[pscustomobject]@{path=$relative;bytes=$_.Length;sha256=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}}
$manifest=[ordered]@{schemaVersion=1;product='CodexDragonPublic';files=$records};SaveJson 'release-manifest.json' $manifest
$manifestText=[IO.File]::ReadAllText((Join-Path $root 'release-manifest.json'))
Save 'output/wallpaper-readiness.log' '2026-10-24T10:00:00Z phase=38 elapsedMs=15 code=0 sourcePhase=6 layoutReason=main-count layoutSupported=0 ignored=C:\Users\PRIVATE_SENTRY\apiKey=PRIVATE_SENTRY'
Save 'output/private.log' 'PRIVATE_SENTRY sk-proj-PRIVATE_SENTRY account=PRIVATE_SENTRY'
$before=@{};foreach($item in $records){$before[$item.path]=(Get-FileHash -LiteralPath (Join-Path $root $item.path) -Algorithm SHA256).Hash}
$ps51=Join-Path $env:WINDIR 'System32\WindowsPowerShell\v1.0\powershell.exe'
Add-Type -AssemblyName System.IO.Compression.FileSystem
function RunCase([string]$Name,[string]$Mode='Detailed'){
    $watch=[Diagnostics.Stopwatch]::StartNew();$output=@(& $ps51 -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'Diagnostics\Run-Diagnostics.ps1') -Mode $Mode 2>&1)
    Check ($LASTEXITCODE -eq 0) ($Name+' generates report despite verification state')
    $path=Join-Path $root 'Diagnostics\Reports\latest-report.zip';$archive=[IO.Compression.ZipFile]::OpenRead($path)
    $parts=@{};try{Check ($archive.Entries.Count -eq 3) ($Name+' has exactly three sanitized report members');foreach($entry in $archive.Entries){Check ($entry.FullName -cin @('numeric-report.json','local-file-check.json','analysis.txt')) ($Name+' fixed report member');$reader=[IO.StreamReader]::new($entry.Open());try{$parts[$entry.FullName]=$reader.ReadToEnd()}finally{$reader.Dispose()}}}finally{$archive.Dispose()}
    $combined=$parts.Values -join "`n"
    Check ($combined -notmatch 'PRIVATE_SENTRY|apiKey=|sk-proj-|C:\\Users\\|DeviceID|SerialNumber' -and -not $combined.Contains($root) -and -not $combined.Contains($ToolsRoot)) ($Name+' excludes paths and private sentinels')
    $report=$parts['numeric-report.json']|ConvertFrom-Json;$check=$parts['local-file-check.json']|ConvertFrom-Json
    Check ($report.environment.kind -ceq 'CodexEnvironmentEvidence') ($Name+' includes bounded environment even on fallback')
    Check ([IO.File]::Exists((Join-Path $root 'Diagnostics\Reports\latest-analysis.txt'))) ($Name+' writes fixed analysis path')
    Check (@(Get-ChildItem -LiteralPath (Join-Path $root 'Diagnostics\Reports') -File -Filter '*.zip').Count -eq 1 -and @(Get-ChildItem -LiteralPath (Join-Path $root 'Diagnostics\Reports') -File -Filter '*.tmp').Count -eq 0) ($Name+' replaces only latest report without archive accumulation')
    $script:scenarios+=[pscustomobject]@{name=$Name;mode=$Mode;elapsedMs=$watch.ElapsedMilliseconds;fileCheckState=$check.state;fileCheckPassed=$check.passed;failureKind=$check.failureKind;failedFileRole=$check.failedFileRole}
    return [pscustomobject]@{report=$report;check=$check}
}
$good=RunCase 'valid'
Check ($good.check.state -ceq 'current-attempt' -and $good.check.passed -and $good.check.fileCount -eq $records.Count -and $good.check.failureKind -ceq 'none' -and $good.check.failedFileIndex -eq -1) 'valid exact integrity and metadata receipt'
foreach($file in $before.Keys){Check ((Get-FileHash -LiteralPath (Join-Path $root $file) -Algorithm SHA256).Hash -ceq $before[$file]) 'diagnostic leaves package input bytes unchanged'}
$ambient=Join-Path $root $media.ambient;$ambientBytes=[IO.File]::ReadAllBytes($ambient)
if(-not ([IO.Path]::GetFullPath($ambient)).StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Fixture deletion escaped isolated root'}
[IO.File]::Delete($ambient)
$missing=RunCase 'missing-media'
$ambientIndex=0;for($i=0;$i -lt $records.Count;$i++){if($records[$i].path -ceq $media.ambient){$ambientIndex=$i;break}}
Check (-not $missing.check.passed -and $missing.check.failureKind -ceq 'missing' -and $missing.check.failedFileRole -ceq 'ambient-media' -and $missing.check.failedFileIndex -eq $ambientIndex) 'missing media localized without path export'
[IO.File]::WriteAllBytes($ambient,$ambientBytes)
[IO.File]::AppendAllText($ambient,'EXTRA')
$size=RunCase 'wrong-size';Check ($size.check.failureKind -ceq 'size-mismatch' -and $size.check.failedFileRole -ceq 'ambient-media') 'size mismatch differentiated'
[IO.File]::WriteAllBytes($ambient,$ambientBytes);$changed=[byte[]]$ambientBytes.Clone();$changed[$changed.Length-1]=$changed[$changed.Length-1] -bxor 1;[IO.File]::WriteAllBytes($ambient,$changed)
$hash=RunCase 'wrong-hash';Check ($hash.check.failureKind -ceq 'hash-mismatch' -and $hash.check.failedFileIndex -eq $ambientIndex) 'same-size hash mismatch differentiated'
[IO.File]::WriteAllBytes($ambient,$ambientBytes)
$heldMedia=[IO.File]::Open($ambient,[IO.FileMode]::Open,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
try{
    $lockedMedia=RunCase 'locked-media'
    Check (-not $lockedMedia.check.passed -and $lockedMedia.check.failureKind -ceq 'read-error' -and $lockedMedia.check.failedFileRole -ceq 'ambient-media') 'locked media is read-error rather than falsely missing or verified'
}finally{$heldMedia.Dispose()}
Save 'release-manifest.json' '{"PRIVATE_SENTRY":"sk-proj-PRIVATE_SENTRY","files":'
$bad=RunCase 'broken-manifest';Check ($bad.report.manifestState -ceq 'invalid-or-missing' -and $bad.check.state -ceq 'current-attempt' -and $bad.check.failureKind -ceq 'manifest-invalid' -and $bad.check.failedFileIndex -eq -1) 'broken manifest still collects this attempt failure and logs'
Check (@($bad.report.logs|Where-Object {$_.log -ceq 'output/wallpaper-readiness.log'})[0].records.Count -eq 1) 'fallback preserves sanitized failure evidence'
$manifestPath=[IO.Path]::GetFullPath((Join-Path $root 'release-manifest.json'));if(-not $manifestPath.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Fixture path escaped root'};[IO.File]::Delete($manifestPath)
$absent=RunCase 'missing-manifest';Check ($absent.report.manifestState -ceq 'invalid-or-missing' -and -not $absent.check.passed -and $absent.check.failureKind -ceq 'manifest-invalid') 'missing manifest still reports'
Save 'release-manifest.json' $manifestText
$again=RunCase 'valid-replacement';Check ($again.check.passed) 'restored fixture overwrites prior failed receipt'
$quick=RunCase 'quick-report' 'Report';Check ($quick.check.state -ceq 'same-manifest' -and $quick.check.passed) 'quick mode reuses matching receipt without verification'
$receiptPath='Diagnostics\Reports\file-check.json'
SaveJson $receiptPath ([ordered]@{schemaVersion=1;passed=$false;fileCount=1;manifestSha256=(Get-FileHash -LiteralPath $manifestPath -Algorithm SHA256).Hash;elapsedMs=1;failure='PRIVATE_SENTRY';failedFileIndex='PRIVATE_SENTRY';failedFileRole='PRIVATE_SENTRY';failureKind='PRIVATE_SENTRY';rawSecret='PRIVATE_SENTRY'})
$poison=RunCase 'poisoned-receipt' 'Report';Check ($poison.check.state -ceq 'same-manifest' -and $poison.check.failure -ceq 'unknown' -and $poison.check.failedFileIndex -eq -1 -and $poison.check.failedFileRole -ceq 'none' -and $poison.check.failureKind -ceq 'unknown') 'old receipt fields are projected through strict allowlists'
SaveJson $receiptPath ([ordered]@{schemaVersion=1;passed=$true;fileCount=12;manifestSha256=('A'*64);elapsedMs=1;failure='none'})
$stale=RunCase 'stale-receipt' 'Report';Check ($stale.check.state -ceq 'not-run' -and -not $stale.check.passed) 'stale manifest does not invent a successful check'
$reportPath=Join-Path $root 'Diagnostics\Reports\latest-report.zip';$reportHash=(Get-FileHash -LiteralPath $reportPath -Algorithm SHA256).Hash
$heldLock=[IO.File]::Open((Join-Path $root 'Diagnostics\Reports\diagnostics.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None)
try{
    $clock=[Diagnostics.Stopwatch]::StartNew()
    $lockOutput=@(& $ps51 -NoProfile -ExecutionPolicy Bypass -File (Join-Path $root 'Diagnostics\Run-Diagnostics.ps1') -Mode Detailed 2>&1)
    Check ($LASTEXITCODE -eq 2 -and $clock.ElapsedMilliseconds -lt 5000) 'second diagnostic exits promptly instead of queuing or racing report'
    Check ((Get-FileHash -LiteralPath $reportPath -Algorithm SHA256).Hash -ceq $reportHash) 'busy diagnostic leaves prior report byte-for-byte unchanged'
    Check (($lockOutput -join ' ') -notmatch 'PRIVATE_SENTRY|apiKey=') 'busy diagnostic does not echo private fields'
}finally{$heldLock.Dispose()}
$runSource=[IO.File]::ReadAllText((Join-Path $ToolsRoot 'Diagnostics\Run-Diagnostics.ps1'))
Check ($runSource.Contains('8388608') -and -not $runSource.Contains('2097152')) 'report cap is 8 MiB'
Check ([IO.File]::ReadAllText((Join-Path $ToolsRoot 'Diagnostics\One-Click-Diagnostics.cmd')).Contains('-Mode Detailed')) 'one-click defaults to detailed mode'
Check ($runSource -notmatch '(?im)^\s*(?:Start-Process|Stop-Process|Start-Job|Invoke-WebRequest|Invoke-RestMethod|Start-Sleep|Wait-Process)\b') 'diagnostic has no application process network or sleep commands'
$receipt=[ordered]@{passed=$script:checks;scenarios=$script:scenarios;sourceSha256=(Get-FileHash -LiteralPath (Join-Path $ToolsRoot 'Diagnostics\Run-Diagnostics.ps1') -Algorithm SHA256).Hash;testSha256=(Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash}
[IO.File]::WriteAllText((Join-Path $WorkRoot 'results.json'),($receipt|ConvertTo-Json -Depth 6),[Text.UTF8Encoding]::new($true))
Write-Host ('PASS detailed diagnostics checks='+$script:checks+'; scenarios='+$script:scenarios.Count)
