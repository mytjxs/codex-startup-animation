[CmdletBinding()]
param([switch]$RemoveGeneratedConfiguration)
. (Join-Path $PSScriptRoot 'PackageTools.ps1')
Assert-SupportedPlatform
$root=(Assert-RegularPath $PSScriptRoot).TrimEnd('\')
$publicGate=$null;$maintenanceGate=$null
try {
    $publicGate=Enter-PackageGate ('Local\CodexDragon.PublicLaunch.'+(Get-TextHash $root.ToUpperInvariant()).ToLowerInvariant().Substring(0,24))
    $identity=[Security.Principal.WindowsIdentity]::GetCurrent()
    try {$sid=$identity.User.Value}finally {$identity.Dispose()}
    $maintenanceGate=Enter-PackageGate ('Local\CodexDragon.MaintenanceLaunch.'+(Get-TextHash $sid).Substring(0,16)+'.'+(Get-TextHash $root.ToUpperInvariant()).Substring(0,16))
$statePath=Join-SafePath $root 'install-state.json'
if(-not (Test-Path -LiteralPath $statePath)){throw 'No public installation ownership record. This may be an extracted release; no files were removed.'}
$state=Get-Content -LiteralPath $statePath -Raw -Encoding UTF8|ConvertFrom-Json
if($state.schemaVersion -ne 1 -or $state.product -cne 'CodexDragonPublic' -or -not [string]::Equals($state.root,$root,[StringComparison]::OrdinalIgnoreCase) -or (Get-Sha256 (Join-Path $root 'release-manifest.json')) -cne $state.manifestSha256){throw 'Installation ownership or manifest mismatch; no files were removed.'}
foreach($name in @('Launch','Start-Codex-Dragon','WallpaperControls','WallpaperHost')) {
    foreach($process in [Diagnostics.Process]::GetProcessesByName($name)) {
        try {
            if(-not $process.HasExited -and $process.MainModule.FileName.StartsWith($root+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'This installation is still running. Exit it normally before uninstalling.'}
        }catch [ComponentModel.Win32Exception] {throw 'Unable to verify running component ownership; no files were removed.'}
        finally {$process.Dispose()}
    }
}
$manifest=Read-PackageManifest $root
$ownedDirectories=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
foreach($record in $manifest.files) {
    $item=Join-SafePath $root $record.path
    for($parent=[IO.Path]::GetDirectoryName($item);$parent -and $parent -cne $root;$parent=[IO.Path]::GetDirectoryName($parent)) {[void]$ownedDirectories.Add($parent)}
}
$preserved=[Collections.Generic.List[string]]::new()
# Delete only a manifest-owned file whose current bytes still match. A changed
# config, unknown user file or diagnostic is deliberately retained.
foreach($record in $manifest.files) {
    $file=Join-SafePath $root $record.path
    if(Test-Path -LiteralPath $file -PathType Leaf) {
        if((Get-Sha256 $file) -ceq $record.sha256){Remove-Item -LiteralPath $file -Force}
        else {$preserved.Add($record.path)}
    }
}
if($null -ne $state.shortcut) {
    $link=Assert-RegularPath $state.shortcut.path
    $expected=Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'Codex Dragon (community).lnk'
    if($link -ceq $expected -and (Test-Path -LiteralPath $link) -and (Get-Sha256 $link) -ceq $state.shortcut.sha256){Remove-Item -LiteralPath $link -Force}
}
if($RemoveGeneratedConfiguration) {
    $config=Join-SafePath $root 'wallpaper-host-config.json'
    if(Test-Path -LiteralPath $config){Remove-Item -LiteralPath $config -Force}
}
Remove-Item -LiteralPath (Join-Path $root 'release-manifest.json') -Force
Remove-Item -LiteralPath $statePath -Force
$dirs=@($ownedDirectories|Sort-Object {$_.Length} -Descending)
foreach($dir in $dirs){[void](Assert-RegularPath $dir);if((Test-Path -LiteralPath $dir -PathType Container) -and @(Get-ChildItem -LiteralPath $dir -Force).Count -eq 0){[IO.Directory]::Delete($dir)}}
if(@(Get-ChildItem -LiteralPath $root -Force).Count -eq 0){[IO.Directory]::Delete($root)}
Write-Output ('Uninstalled manifest-owned files. Preserved modified entries: '+$preserved.Count+'. Logs, changed configurations and unrelated files may remain.')
Write-Output 'No official Codex files, accounts, system settings, or running clients were changed.'
}finally {
    if($null -ne $maintenanceGate){try{$maintenanceGate.ReleaseMutex()}finally{$maintenanceGate.Dispose()}}
    if($null -ne $publicGate){try{$publicGate.ReleaseMutex()}finally{$publicGate.Dispose()}}
}
