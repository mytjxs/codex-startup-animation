[CmdletBinding()]
param(
    [Parameter(Mandatory=$true)][string]$MediaRoot,
    [string]$OutputDirectory=(Join-Path $PSScriptRoot 'dist\CodexDragon'),
    [switch]$SkipZip
)
. (Join-Path $PSScriptRoot 'packaging\PackageTools.ps1')
Assert-SupportedPlatform
$source=Assert-RegularPath $PSScriptRoot
$media=Assert-RegularPath $MediaRoot
$output=Assert-RegularPath $OutputDirectory
if(Test-Path -LiteralPath $output){throw 'Build output already exists. Choose a new output directory; this build never erases an existing release.'}
$mediaManifest=Get-Content -LiteralPath (Join-Path $source 'media-manifest.json') -Raw -Encoding UTF8|ConvertFrom-Json
if($mediaManifest.schemaVersion -ne 1 -or @($mediaManifest.files).Count -ne 46){throw 'Unsupported complete media inventory.'}
foreach($record in $mediaManifest.files) {
    $path=Join-SafePath $media $record.path
    if(-not (Test-Path -LiteralPath $path -PathType Leaf) -or (Get-Item -LiteralPath $path).Length -ne $record.bytes -or (Get-Sha256 $path) -cne $record.sha256){throw ('Media archive incomplete or corrupt: '+$record.path)}
}
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler=Join-Path $framework 'csc.exe'
if(-not (Test-Path -LiteralPath $compiler)){throw 'The Windows .NET Framework compiler is missing. Repair .NET Framework 4.8.'}
[IO.Directory]::CreateDirectory($output)|Out-Null
$build=Join-Path $output 'build';[IO.Directory]::CreateDirectory($build)|Out-Null
$launcherSources=@('DragonLauncher.cs','CinematicScene.cs','StrokeTrace.cs','FilmSplash.cs','FilmSplashV2.cs','NativeHandoff.cs','PulseReveal.cs','EyeRig.cs','BridgeActivation.cs','StartupReliability.cs','NativeRestoreGuard.cs')
$hostSources=@('Program.cs','CdpTransport.cs','ControlPipe.cs','NativeHost.cs','WallpaperSession.cs')
$references=@('mscorlib.dll','System.dll','System.Core.dll','System.Configuration.dll','System.Drawing.dll','System.Runtime.Serialization.dll','System.Security.dll','System.Web.Extensions.dll','System.Windows.Forms.dll','System.Xaml.dll','System.Xml.dll','System.Xml.Linq.dll','WPF/PresentationCore.dll','WPF/PresentationFramework.dll','WPF/WindowsBase.dll')
$compiled=[Collections.Generic.List[object]]::new()
function Compile([string]$Name,[string]$Entry,[string[]]$Sources) {
    $arguments=@('/nologo','/noconfig','/nostdlib+','/target:winexe','/platform:x64','/optimize+','/codepage:65001','/warn:4','/warnaserror+',('/out:'+(Join-Path $output $Name)),('/main:'+$Entry),('/win32manifest:'+(Join-Path $source 'build-inputs\Runtime.manifest')))
    $arguments+=@($references|ForEach-Object {'/r:'+(Join-Path $framework $_)})
    $arguments+=(Join-Path $source 'build-inputs\RuntimeMetadata.cs')
    $arguments+=$Sources
    $start=[Diagnostics.ProcessStartInfo]::new();$start.FileName=$compiler
    $start.Arguments=(@($arguments|ForEach-Object {if($_.Contains('"')){throw 'Quotes in a compiler path are unsupported.'};'"'+$_+'"'}) -join ' ')
    $start.WorkingDirectory=$source;$start.UseShellExecute=$false;$start.CreateNoWindow=$true;$start.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden;$start.RedirectStandardOutput=$true;$start.RedirectStandardError=$true
    $process=[Diagnostics.Process]::Start($start)
    try {
        $stdout=$process.StandardOutput.ReadToEndAsync();$stderr=$process.StandardError.ReadToEndAsync()
        if(-not $process.WaitForExit(60000)){try{$process.Kill()}catch{};throw ('Compiler timed out: '+$Name)}
        $messages=$stdout.GetAwaiter().GetResult()+$stderr.GetAwaiter().GetResult()
        if($process.ExitCode -ne 0){throw ('Compile failed: '+$Name+[Environment]::NewLine+$messages)}
    }finally {$process.Dispose()}
    $assembly=[Reflection.Assembly]::ReflectionOnlyLoad([IO.File]::ReadAllBytes((Join-Path $output $Name)))
    $attribute=@($assembly.GetCustomAttributesData()|Where-Object {$_.AttributeType.FullName -ceq 'System.Runtime.Versioning.TargetFrameworkAttribute'})
    if($attribute.Count -ne 1 -or $attribute[0].ConstructorArguments[0].Value -cne '.NETFramework,Version=v4.8'){throw 'Compiled framework metadata mismatch.'}
    $compiled.Add([ordered]@{name=$Name;entryPoint=$Entry;sha256=Get-Sha256 (Join-Path $output $Name);targetFramework='.NETFramework,Version=v4.8';platform='x64'})
}
$launchFiles=@($launcherSources|ForEach-Object {Join-Path $source ('src/'+$_)})
$hostFiles=@($hostSources|ForEach-Object {Join-Path $source ('wallpaper-host-src/'+$_)})
Compile 'Start-Codex-Dragon.exe' 'DragonLauncher.Program' $launchFiles
Compile 'WallpaperControls.exe' 'DragonLauncher.WallpaperControls' ($launchFiles+@(Join-Path $source 'src/WallpaperControls.cs'))
Compile 'WallpaperHost.exe' 'CodexDragonWallpaper.Program' $hostFiles
Copy-Item -LiteralPath (Join-Path $source 'renderer.js') -Destination $output
Copy-Item -LiteralPath (Join-Path $source 'launcher.config.xml') -Destination $output
foreach($record in $mediaManifest.files) {
    $target=Join-SafePath $output $record.path;[IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($target))|Out-Null
    [IO.File]::Copy((Join-SafePath $media $record.path),$target,$false)
}
$pins=[Text.StringBuilder]::new()
[void]$pins.AppendLine('using System.Collections.Generic; namespace CodexDragonPublic { internal sealed class MediaPin { internal string Path,Sha256; internal long Bytes; } internal static class BootstrapPins { internal static readonly Dictionary<string,string> Code=new Dictionary<string,string> {')
foreach($name in @('Start-Codex-Dragon.exe','WallpaperControls.exe','WallpaperHost.exe','renderer.js')){[void]$pins.AppendLine(('{{"{0}","{1}"}},' -f $name,(Get-Sha256 (Join-Path $output $name))))}
[void]$pins.AppendLine('}; internal static readonly MediaPin[] Media=new MediaPin[]{')
foreach($record in $mediaManifest.files){[void]$pins.AppendLine(('new MediaPin{{Path="{0}",Bytes={1}L,Sha256="{2}"}},' -f $record.path,$record.bytes,$record.sha256))}
[void]$pins.AppendLine('}; } }')
$pinsPath=Join-Path $build 'BootstrapPins.cs';Write-Utf8 $pinsPath $pins.ToString()
Compile 'Launch.exe' 'CodexDragonPublic.PortableLaunch' ($hostFiles+@((Join-Path $source 'packaging/PortableLaunch.cs'),$pinsPath))
foreach($name in @('Install.ps1','Uninstall.ps1','PackageTools.ps1')){Copy-Item -LiteralPath (Join-Path $source ('packaging/'+$name)) -Destination $output}
Copy-Item -LiteralPath (Join-Path $source 'README.txt') -Destination $output
Copy-Item -LiteralPath (Join-Path $source 'FONT-LICENSE.txt') -Destination $output
# Include the complete reviewed source in the user ZIP; no history, credentials,
# previous deployments, application binaries, profiles or local logs are copied.
$codeTarget=Join-Path $output 'source';[IO.Directory]::CreateDirectory($codeTarget)|Out-Null
$codeFiles=@('Build.ps1','README.txt','FONT-LICENSE.txt','media-manifest.json','launcher.config.xml','renderer.js','.gitignore','SOURCE-PROVENANCE.txt')
foreach($name in $codeFiles){Copy-Item -LiteralPath (Join-Path $source $name) -Destination $codeTarget}
foreach($folder in @('src','wallpaper-host-src','packaging','build-inputs','tests')){Copy-Item -LiteralPath (Join-Path $source $folder) -Destination $codeTarget -Recurse}
Write-Utf8 (Join-Path $output 'build/build-receipt.json') (@{schemaVersion=1;compiler='Windows .NET Framework 4.x csc.exe';components=@($compiled);sourcePathsAreRelative=$true;fullMediaVerified=$true}|ConvertTo-Json -Depth 6)
$records=@(Get-ChildItem -LiteralPath $output -Recurse -File|Sort-Object FullName|ForEach-Object {[ordered]@{path=$_.FullName.Substring($output.Length+1).Replace('\','/');bytes=$_.Length;sha256=Get-Sha256 $_.FullName}})
Write-Utf8 (Join-Path $output 'release-manifest.json') (@{schemaVersion=1;product='CodexDragonPublic';version='34.1-portable-candidate';files=$records}|ConvertTo-Json -Depth 6)
Test-PackageFiles $output (Read-PackageManifest $output)
if(-not $SkipZip) {
    $zip=$output+'.zip';if(Test-Path -LiteralPath $zip){throw 'ZIP already exists; choose a fresh output directory.'}
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [IO.Compression.ZipFile]::CreateFromDirectory($output,$zip,[IO.Compression.CompressionLevel]::Optimal,$false)
    Write-Utf8 ($zip+'.sha256') ((Get-Sha256 $zip)+'  '+[IO.Path]::GetFileName($zip)+[Environment]::NewLine)
}
Write-Output ('PASS: rebuilt four executables and verified '+$records.Count+' complete release files.')
