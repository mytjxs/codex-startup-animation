param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$sourceRoot=Split-Path -Parent (Split-Path -Parent $PSCommandPath)
$output=[IO.Path]::GetFullPath($OutputDirectory)
[void][IO.Directory]::CreateDirectory($output)
$exe=Join-Path $output 'RuntimeDiagnosticsTests.exe'
$hostFiles=@(Get-ChildItem -LiteralPath (Join-Path $sourceRoot 'wallpaper-host-src') -Filter '*.cs'|ForEach-Object FullName)+@(Join-Path $sourceRoot 'src\OfficialIdentity.cs')
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $compiler /nologo /target:exe /platform:x64 /main:CodexDragonWallpaper.RuntimeDiagnosticsTests /r:System.Web.Extensions.dll ('/win32manifest:'+(Join-Path $sourceRoot 'build-inputs\Runtime.manifest')) ('/out:'+$exe) @hostFiles (Join-Path $sourceRoot 'build-inputs\RuntimeMetadata.cs') (Join-Path $sourceRoot 'tests\RuntimeDiagnosticsTests.cs')
if($LASTEXITCODE -ne 0){throw 'Diagnostic test build failed'}
$result=& $exe
$code=$LASTEXITCODE
[IO.File]::WriteAllText((Join-Path $output 'results.json'),($result -join [Environment]::NewLine),[Text.UTF8Encoding]::new($false))
$result
exit $code
