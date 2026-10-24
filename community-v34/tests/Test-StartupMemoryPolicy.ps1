[CmdletBinding()]
param([string]$SourceRoot,[Parameter(Mandatory=$true)][string]$OutputDirectory,[switch]$LegacyProductionPolicy)
$ErrorActionPreference='Stop'
$testDirectory=Split-Path -Parent $PSCommandPath
if([string]::IsNullOrWhiteSpace($SourceRoot)){$SourceRoot=Split-Path -Parent $testDirectory}
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler=Join-Path $framework 'csc.exe'
$sources=@('DragonLauncher.cs','CinematicScene.cs','StrokeTrace.cs','FilmSplash.cs','FilmSplashV2.cs','NativeHandoff.cs','PulseReveal.cs','EyeRig.cs','BridgeActivation.cs','StartupReliability.cs','NativeRestoreGuard.cs','OfficialIdentity.cs')
$references=@('mscorlib.dll','System.dll','System.Core.dll','System.Configuration.dll','System.Drawing.dll','System.Runtime.Serialization.dll','System.Security.dll','System.Web.Extensions.dll','System.Windows.Forms.dll','System.Xaml.dll','System.Xml.dll','System.Xml.Linq.dll','WPF/PresentationCore.dll','WPF/PresentationFramework.dll','WPF/WindowsBase.dll')
[void][IO.Directory]::CreateDirectory($OutputDirectory)
$assembly=Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) 'StartupMemoryPolicyTests.exe'
$arguments=@('/nologo','/noconfig','/nostdlib+','/target:exe','/platform:x64','/optimize+','/codepage:65001','/warn:4','/warnaserror+','/main:DragonLauncher.StartupMemoryPolicyTests',('/out:'+$assembly))
if($LegacyProductionPolicy){$arguments+='/define:LEGACY_PHYSICAL_POLICY'}
$arguments+=('/win32manifest:'+(Join-Path $SourceRoot 'build-inputs/Runtime.manifest'))
$arguments+=(Join-Path $SourceRoot 'build-inputs/RuntimeMetadata.cs')
$arguments+=@($references|ForEach-Object {'/r:'+(Join-Path $framework $_)})
$arguments+=@($sources|ForEach-Object {Join-Path $SourceRoot ('src/'+$_)})
$arguments+=(Join-Path $testDirectory 'StartupMemoryPolicyTests.cs')
Get-FileHash -LiteralPath (Join-Path $SourceRoot 'src/StartupReliability.cs') -Algorithm SHA256|Select-Object Hash|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $OutputDirectory 'source-hash.json') -Encoding UTF8
& $compiler @arguments
if($LASTEXITCODE -ne 0){throw 'Startup memory policy test compilation failed'}
$result=@(& $assembly);$resultCode=$LASTEXITCODE
$result|Set-Content -LiteralPath (Join-Path $OutputDirectory 'results.txt') -Encoding UTF8
$result
exit $resultCode
