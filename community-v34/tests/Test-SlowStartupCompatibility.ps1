[CmdletBinding()]
param([string]$SourceRoot,[Parameter(Mandatory=$true)][string]$OutputDirectory)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$testDirectory=[IO.Path]::GetDirectoryName($PSCommandPath)
if([string]::IsNullOrWhiteSpace($SourceRoot)){$SourceRoot=[IO.Path]::GetDirectoryName($testDirectory)}
$SourceRoot=[IO.Path]::GetFullPath($SourceRoot).TrimEnd('\')
$outputRoot=[IO.Path]::GetFullPath($OutputDirectory).TrimEnd('\')
if($outputRoot -ieq $SourceRoot -or $outputRoot.StartsWith($SourceRoot+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Test output must be outside source'}
[void][IO.Directory]::CreateDirectory($outputRoot)
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler=Join-Path $framework 'csc.exe'
$sources=@('DragonLauncher.cs','CinematicScene.cs','StrokeTrace.cs','FilmSplash.cs','FilmSplashV2.cs','NativeHandoff.cs','PulseReveal.cs','EyeRig.cs','BridgeActivation.cs','StartupReliability.cs','NativeRestoreGuard.cs','OfficialIdentity.cs')
$references=@('mscorlib.dll','System.dll','System.Core.dll','System.Configuration.dll','System.Drawing.dll','System.Runtime.Serialization.dll','System.Security.dll','System.Web.Extensions.dll','System.Windows.Forms.dll','System.Xaml.dll','System.Xml.dll','System.Xml.Linq.dll','WPF/PresentationCore.dll','WPF/PresentationFramework.dll','WPF/WindowsBase.dll')
$assembly=Join-Path $outputRoot 'SlowStartupCompatibilityTests.exe'
$arguments=@('/nologo','/noconfig','/nostdlib+','/target:exe','/platform:x64','/optimize+','/codepage:65001','/warn:4','/warnaserror+','/main:DragonLauncher.SlowStartupCompatibilityTests',('/out:'+$assembly))
$arguments+=('/win32manifest:'+(Join-Path $SourceRoot 'build-inputs\Runtime.manifest'))
$arguments+=(Join-Path $SourceRoot 'build-inputs\RuntimeMetadata.cs')
$arguments+=@($references|ForEach-Object {'/r:'+(Join-Path $framework $_)})
$arguments+=@($sources|ForEach-Object {Join-Path $SourceRoot ('src\'+$_)})
$arguments+=(Join-Path $testDirectory 'SlowStartupCompatibilityTests.cs')
& $compiler @arguments
if($LASTEXITCODE -ne 0){throw 'Slow compatibility test compilation failed'}
$output=@(& $assembly);$code=$LASTEXITCODE
[IO.File]::WriteAllText((Join-Path $outputRoot 'results.json'),($output -join [Environment]::NewLine),[Text.UTF8Encoding]::new($true))
$hashes=@(foreach($relative in @($sources|ForEach-Object {'src/'+$_})+@('build-inputs/Runtime.manifest','build-inputs/RuntimeMetadata.cs','tests/SlowStartupCompatibilityTests.cs','tests/Test-SlowStartupCompatibility.ps1')){[pscustomobject]@{path=$relative;sha256=(Get-FileHash -LiteralPath (Join-Path $SourceRoot $relative) -Algorithm SHA256).Hash}})
[IO.File]::WriteAllText((Join-Path $outputRoot 'source-hashes.json'),($hashes|ConvertTo-Json),[Text.UTF8Encoding]::new($true))
$output
exit $code
