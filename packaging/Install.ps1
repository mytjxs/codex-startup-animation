[CmdletBinding()]
param(
    [string]$Destination=(Join-Path $env:LOCALAPPDATA 'Programs\CodexDragonPublic'),
    [switch]$CreateDesktopShortcut,
    [switch]$SkipConfiguration
)
. (Join-Path $PSScriptRoot 'PackageTools.ps1')
Assert-SupportedPlatform
$source=Assert-RegularPath $PSScriptRoot
$destination=(Assert-RegularPath $Destination).TrimEnd('\')
if(Test-Path -LiteralPath $destination){throw 'Destination already exists. Choose a new empty path or uninstall that public installation first. No existing files were changed.'}
if($destination.Length -lt 10 -or $destination -eq [IO.Path]::GetPathRoot($destination) -or $destination -eq $env:USERPROFILE -or $destination.StartsWith($source+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Unsafe installation destination.'}
$manifest=Read-PackageManifest $source
Test-PackageFiles $source $manifest
$parent=[IO.Path]::GetDirectoryName($destination)
[IO.Directory]::CreateDirectory($parent)|Out-Null
$staged=Assert-RegularPath (Join-Path $parent ('.'+[IO.Path]::GetFileName($destination)+'.install-'+[Guid]::NewGuid().ToString('N')))
[IO.Directory]::CreateDirectory($staged)|Out-Null
try {
    foreach($record in $manifest.files) {
        $target=Join-SafePath $staged $record.path
        [IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))|Out-Null
        [IO.File]::Copy((Join-SafePath $source $record.path),$target,$false)
    }
    [IO.File]::Copy((Join-Path $source 'release-manifest.json'),(Join-Path $staged 'release-manifest.json'),$false)
    Test-PackageFiles $staged $manifest
    Write-Utf8 (Join-Path $staged 'install-state.json') (@{schemaVersion=1;product='CodexDragonPublic';root=$destination;manifestSha256=Get-Sha256 (Join-Path $staged 'release-manifest.json');shortcut=$null}|ConvertTo-Json -Depth 5)
    # Promote only a fully copied, verified tree. Nothing is started during installation.
    [IO.Directory]::Move($staged,$destination)
}catch {
    # Failed copies are kept separately for inspection; never recursively erase a
    # computed directory, a previous installation, or an unknown user's file.
    throw 'Installation did not complete. A separate .install-* staging directory may remain. No existing installation was overwritten.'
}
$configured=$false
if(-not $SkipConfiguration) {
    $start=[Diagnostics.ProcessStartInfo]::new();$start.FileName=Join-Path $destination 'Launch.exe';$start.Arguments='--configure'
    $start.WorkingDirectory=$destination;$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden
    $process=[Diagnostics.Process]::Start($start)
    try {
        if(-not $process.WaitForExit(25000)){try{$process.Kill()}catch{};Write-Warning 'Official Codex configuration timed out. The files are installed; run Launch.exe after prerequisites are ready.'}
        else {$configured=$process.ExitCode -eq 0}
    }finally {$process.Dispose()}
    if(-not $configured){Write-Warning 'Files installed, but official Codex registration/signature could not be verified. Install the Microsoft Store x64 Codex app, then use Launch.exe. No client was launched.'}
}
if($CreateDesktopShortcut) {
    $desktop=[Environment]::GetFolderPath('DesktopDirectory')
    $link=Assert-RegularPath (Join-Path $desktop 'Codex Dragon (community).lnk')
    if(Test-Path -LiteralPath $link){Write-Warning 'The community shortcut already exists and was not replaced.'}
    else {
        $shell=New-Object -ComObject WScript.Shell;$shortcut=$null
        try {$shortcut=$shell.CreateShortcut($link);$shortcut.TargetPath=Join-Path $destination 'Launch.exe';$shortcut.WorkingDirectory=$destination;$shortcut.Description='Community startup animation and wallpaper for Codex';$shortcut.Save()}
        finally {if($null -ne $shortcut){[void][Runtime.InteropServices.Marshal]::ReleaseComObject($shortcut)};[void][Runtime.InteropServices.Marshal]::ReleaseComObject($shell)}
        $state=Get-Content -LiteralPath (Join-Path $destination 'install-state.json') -Raw|ConvertFrom-Json
        $state.shortcut=[ordered]@{path=$link;sha256=Get-Sha256 $link}
        Write-Utf8 (Join-Path $destination 'install-state.json') ($state|ConvertTo-Json -Depth 5)
    }
}
Write-Output ('Installed complete release. Start with: '+(Join-Path $destination 'Launch.exe'))
Write-Output 'The official Codex app, its original icon, account data, and running sessions were not modified.'
