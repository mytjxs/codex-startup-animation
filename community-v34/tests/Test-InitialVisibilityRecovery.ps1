[CmdletBinding()]
param([string]$SourceRoot,[Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$testDirectory=Split-Path -Parent $PSCommandPath
if([string]::IsNullOrWhiteSpace($SourceRoot)){$SourceRoot=Split-Path -Parent $testDirectory}
[void][IO.Directory]::CreateDirectory($OutputDirectory)
$outputRoot=[IO.Path]::GetFullPath($OutputDirectory)
$assembly=Join-Path $outputRoot 'InitialVisibilityRecoveryTests.exe'
$source=Join-Path $SourceRoot 'wallpaper-host-src/NativeHost.cs'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET/Framework64/v4.0.30319/csc.exe'
& $compiler /nologo /target:exe /platform:x64 /optimize+ /warn:4 /warnaserror+ /main:CodexDragonWallpaper.InitialVisibilityRecoveryTests ('/out:'+$assembly) $source (Join-Path $testDirectory 'InitialVisibilityRecoveryTests.cs')
if($LASTEXITCODE -ne 0){throw 'Initial visibility fixture compilation failed'}
Get-FileHash -LiteralPath $source -Algorithm SHA256 | Select-Object Hash | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $outputRoot 'source-hash.json') -Encoding UTF8
$results=@(& $assembly);$code=$LASTEXITCODE
$results | Set-Content -LiteralPath (Join-Path $outputRoot 'results.txt') -Encoding UTF8
$results
exit $code
