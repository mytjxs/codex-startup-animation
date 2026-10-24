[CmdletBinding()]
param([string]$SourceRoot,[Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$testDirectory=Split-Path -Parent $PSCommandPath
if([string]::IsNullOrWhiteSpace($SourceRoot)){$SourceRoot=Split-Path -Parent $testDirectory}
$sourceFull=[IO.Path]::GetFullPath($SourceRoot).TrimEnd('\','/')
$outputFull=[IO.Path]::GetFullPath($OutputDirectory).TrimEnd('\','/')
if($outputFull -eq $sourceFull -or $outputFull.StartsWith($sourceFull+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Test artifacts must remain outside source project'}
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$sources=@('DragonLauncher.cs','CinematicScene.cs','StrokeTrace.cs','FilmSplash.cs','FilmSplashV2.cs','NativeHandoff.cs','PulseReveal.cs','EyeRig.cs','BridgeActivation.cs','StartupReliability.cs','NativeRestoreGuard.cs','OfficialIdentity.cs')
$references=@('mscorlib.dll','System.dll','System.Core.dll','System.Configuration.dll','System.Drawing.dll','System.Runtime.Serialization.dll','System.Security.dll','System.Web.Extensions.dll','System.Windows.Forms.dll','System.Xaml.dll','System.Xml.dll','System.Xml.Linq.dll','WPF/PresentationCore.dll','WPF/PresentationFramework.dll','WPF/WindowsBase.dll')
$fixtureDirectory=Join-Path $outputFull ('fixture-'+[Guid]::NewGuid().ToString('N'))
[void][IO.Directory]::CreateDirectory($fixtureDirectory)
$assembly=Join-Path $fixtureDirectory 'StartupLifecycleRecoveryTests.exe'
$arguments=@('/nologo','/noconfig','/nostdlib+','/target:exe','/platform:x64','/optimize+','/codepage:65001','/warn:4','/warnaserror+','/main:DragonLauncher.StartupLifecycleRecoveryTests',('/out:'+$assembly))
$arguments+=('/win32manifest:'+(Join-Path $SourceRoot 'build-inputs/Runtime.manifest'))
$arguments+=(Join-Path $SourceRoot 'build-inputs/RuntimeMetadata.cs')
$arguments+=@($references|ForEach-Object {'/r:'+(Join-Path $framework $_)})
$arguments+=@($sources|ForEach-Object {Join-Path $SourceRoot ('src/'+$_)})
$arguments+=(Join-Path $testDirectory 'StartupLifecycleRecoveryTests.cs')
& (Join-Path $framework 'csc.exe') @arguments
if($LASTEXITCODE -ne 0){throw 'Startup lifecycle recovery compilation failed'}
$result=@(& $assembly $fixtureDirectory);$resultCode=$LASTEXITCODE
[IO.File]::WriteAllText((Join-Path $outputFull 'results.json'),($result -join [Environment]::NewLine),[Text.UTF8Encoding]::new($false))
$hashes=@($sources|ForEach-Object {[pscustomobject]@{file=$_;sha256=(Get-FileHash -LiteralPath (Join-Path $SourceRoot ('src/'+$_)) -Algorithm SHA256).Hash}})
[IO.File]::WriteAllText((Join-Path $outputFull 'source-hashes.json'),($hashes|ConvertTo-Json -Depth 4),[Text.UTF8Encoding]::new($false))
$result
exit $resultCode
