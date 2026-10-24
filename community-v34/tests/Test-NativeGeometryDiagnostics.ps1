[CmdletBinding()]
param([string]$SourceRoot,[Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$testDirectory=Split-Path -Parent $PSCommandPath
if([string]::IsNullOrWhiteSpace($SourceRoot)){$SourceRoot=Split-Path -Parent $testDirectory}
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler=Join-Path $framework 'csc.exe'
$sources=@('DragonLauncher.cs','CinematicScene.cs','StrokeTrace.cs','FilmSplash.cs','FilmSplashV2.cs','NativeHandoff.cs','PulseReveal.cs','EyeRig.cs','BridgeActivation.cs','StartupReliability.cs','NativeRestoreGuard.cs','OfficialIdentity.cs')
$references=@('mscorlib.dll','System.dll','System.Core.dll','System.Configuration.dll','System.Drawing.dll','System.Runtime.Serialization.dll','System.Security.dll','System.Web.Extensions.dll','System.Windows.Forms.dll','System.Xaml.dll','System.Xml.dll','System.Xml.Linq.dll','WPF/PresentationCore.dll','WPF/PresentationFramework.dll','WPF/WindowsBase.dll')
[void][IO.Directory]::CreateDirectory($OutputDirectory)
$assembly=Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) 'NativeGeometryDiagnosticsTests.exe'
$arguments=@('/nologo','/noconfig','/nostdlib+','/target:exe','/platform:x64','/optimize+','/codepage:65001','/warn:4','/warnaserror+','/main:DragonLauncher.NativeGeometryDiagnosticsTests',('/out:'+$assembly))
$arguments+=('/win32manifest:'+(Join-Path $SourceRoot 'build-inputs/Runtime.manifest'))
$arguments+=(Join-Path $SourceRoot 'build-inputs/RuntimeMetadata.cs')
$arguments+=@($references|ForEach-Object {'/r:'+(Join-Path $framework $_)})
$arguments+=@($sources|ForEach-Object {Join-Path $SourceRoot ('src/'+$_)})
$arguments+=(Join-Path $testDirectory 'NativeGeometryDiagnosticsTests.cs')
& $compiler @arguments
if($LASTEXITCODE -ne 0){throw 'Native geometry diagnostic test compilation failed'}
$result=@(& $assembly);$resultCode=$LASTEXITCODE
$result|Set-Content -LiteralPath (Join-Path $OutputDirectory 'results.txt') -Encoding UTF8
$result
exit $resultCode
