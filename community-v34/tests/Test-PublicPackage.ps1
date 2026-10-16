[CmdletBinding()]
param([Parameter(Mandatory=$true)][string]$ReleaseRoot,[switch]$CheckOfficial)
. (Join-Path $PSScriptRoot '..\packaging\PackageTools.ps1')
Assert-SupportedPlatform
$root=Assert-RegularPath $ReleaseRoot
$manifest=Read-PackageManifest $root
Test-PackageFiles $root $manifest
foreach($name in @('Launch.exe','Start-Codex-Dragon.exe','WallpaperControls.exe','WallpaperHost.exe')) {
    $assembly=[Reflection.Assembly]::ReflectionOnlyLoad([IO.File]::ReadAllBytes((Join-Path $root $name)))
    $framework=@($assembly.GetCustomAttributesData()|Where-Object {$_.AttributeType.FullName -ceq 'System.Runtime.Versioning.TargetFrameworkAttribute'})
    if($framework.Count -ne 1 -or $framework[0].ConstructorArguments[0].Value -cne '.NETFramework,Version=v4.8'){throw ('Invalid framework: '+$name)}
}
if($CheckOfficial) {
    $start=[Diagnostics.ProcessStartInfo]::new();$start.FileName=Join-Path $root 'Launch.exe';$start.Arguments='--check';$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden
    $process=[Diagnostics.Process]::Start($start)
    try {if(-not $process.WaitForExit(25000)){try{$process.Kill()}catch{};throw 'Official read-only verification timed out.'};if($process.ExitCode -ne 0){throw 'Official Codex prerequisites not verified. No Codex was started.'}}
    finally {$process.Dispose()}
}
Write-Output ('PASS: '+@($manifest.files).Count+' files and four .NET Framework 4.8 assemblies verified. No Codex client was started or stopped.')
