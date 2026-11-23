Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
function Get-Sha256([string]$Path) { (Get-FileHash -LiteralPath $Path -Algorithm SHA256).Hash }
function Assert-RegularPath([string]$Path) {
    $full=[IO.Path]::GetFullPath($Path)
    if($full.StartsWith('\\') -or $full.Substring(2).Contains(':')){throw 'Local filesystem paths only; UNC paths and alternate data streams are unsupported.'}
    for($part=$full;$part;$part=[IO.Path]::GetDirectoryName($part)) {
        if((Test-Path -LiteralPath $part) -and ((Get-Item -LiteralPath $part -Force).Attributes -band [IO.FileAttributes]::ReparsePoint)) {throw 'Linked installation paths are unsupported.'}
    }
    return $full
}
function Join-SafePath([string]$Root,[string]$Relative) {
    if($Relative -notmatch '^[A-Za-z0-9][A-Za-z0-9._/ -]*$' -or @($Relative.Split('/')|Where-Object {$_ -eq '' -or $_ -eq '.' -or $_ -eq '..' -or $_.EndsWith('.') -or $_.EndsWith(' ')}).Count){throw 'Unsafe package member path.'}
    $base=(Assert-RegularPath $Root).TrimEnd('\')
    $full=Assert-RegularPath (Join-Path $base $Relative)
    if(-not $full.StartsWith($base+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Package member escaped its root.'}
    return $full
}
function Write-Utf8([string]$Path,[string]$Text) { [IO.File]::WriteAllText($Path,$Text,[Text.UTF8Encoding]::new($false)) }
function Get-TextHash([string]$Text) {
    $sha=[Security.Cryptography.SHA256]::Create()
    try {return [BitConverter]::ToString($sha.ComputeHash([Text.Encoding]::UTF8.GetBytes($Text))).Replace('-','')}
    finally {$sha.Dispose()}
}
function Enter-PackageGate([string]$Name) {
    $gate=[Threading.Mutex]::new($false,$Name);$held=$false
    try {
        try {$held=$gate.WaitOne(0)}catch [Threading.AbandonedMutexException] {$held=$true}
        if(-not $held){throw 'This installation is busy. Let startup or maintenance finish before trying again.'}
        return $gate
    }catch {$gate.Dispose();throw}
}
function Read-PackageManifest([string]$Root) {
    $path=Join-SafePath $Root 'release-manifest.json'
    if((Get-Item -LiteralPath $path).Length -gt 262144){throw 'Release manifest too large.'}
    $manifest=Get-Content -LiteralPath $path -Raw -Encoding UTF8|ConvertFrom-Json
    if($manifest.schemaVersion -ne 1 -or $manifest.product -cne 'CodexDragonPublic' -or @($manifest.files).Count -lt 12 -or @($manifest.files).Count -gt 300){throw 'Unsupported release manifest.'}
    $seen=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    foreach($record in $manifest.files) {
        [void](Join-SafePath $Root $record.path)
        if(-not $seen.Add($record.path) -or $record.sha256 -notmatch '^[A-F0-9]{64}$' -or $record.bytes -lt 0){throw 'Invalid release manifest record.'}
    }
    foreach($required in @('Launch.exe','Start-Codex-Dragon.exe','WallpaperControls.exe','WallpaperHost.exe','renderer.js','launcher.config.xml','Install.ps1','Uninstall.ps1','PackageTools.ps1','README.txt')) {
        if(-not $seen.Contains($required)){throw ('Incomplete release: '+$required)}
    }
    return $manifest
}
function Test-PackageFiles([string]$Root,$Manifest) {
    foreach($record in $Manifest.files) {
        $path=Join-SafePath $Root $record.path
        if(-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -ne $record.bytes -or (Get-Sha256 $path) -cne $record.sha256){throw ('Missing or corrupt release component: '+$record.path)}
    }
}
function Assert-SupportedPlatform {
    if($PSVersionTable.PSVersion.Major -ne 5){throw 'Use Windows PowerShell 5.1 (powershell.exe), not PowerShell 7 or a Unix shell.'}
    if(-not [Environment]::Is64BitOperatingSystem -or [Environment]::OSVersion.Version.Major -lt 10){throw 'Windows 10/11 x64 is required.'}
    $net=Get-ItemProperty -LiteralPath 'HKLM:\SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full' -ErrorAction Stop
    if($net.Release -lt 528040){throw '.NET Framework 4.8 or later is required.'}
}
