param([string]$Source,[Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$scriptDirectory=Split-Path -Parent $PSCommandPath
if([string]::IsNullOrWhiteSpace($Source)){$Source=Join-Path (Split-Path -Parent $scriptDirectory) 'wallpaper-host-src'}
$files=@(Get-ChildItem -LiteralPath $Source -Filter '*.cs' | ForEach-Object FullName)
if($files.Count -ne 5){throw 'Expected five host source files'}
$files=@($files)+@(Join-Path (Split-Path -Parent ([IO.Path]::GetFullPath($Source))) 'src\OfficialIdentity.cs')
[void][IO.Directory]::CreateDirectory($OutputDirectory)
$outputRoot=[IO.Path]::GetFullPath($OutputDirectory)
$assembly=Join-Path $outputRoot 'HostMemoryRecoveryTests.exe'
$projectRoot=Split-Path -Parent $scriptDirectory
$metadata=Join-Path $projectRoot 'build-inputs\RuntimeMetadata.cs'
$manifest=Join-Path $projectRoot 'build-inputs\Runtime.manifest'
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
& $csc /nologo /target:exe /platform:x64 /warn:4 /warnaserror+ /main:CodexDragonWallpaper.HostMemoryRecoveryTests /r:System.Web.Extensions.dll ('/win32manifest:'+$manifest) ('/out:'+$assembly) @files $metadata (Join-Path $scriptDirectory 'HostMemoryRecoveryTests.cs')
if($LASTEXITCODE -ne 0){throw 'Compile failed'}
$result=@(& $assembly $outputRoot);$code=$LASTEXITCODE
[IO.File]::WriteAllText((Join-Path $outputRoot 'results.txt'),($result -join [Environment]::NewLine),[Text.UTF8Encoding]::new($false))
$result
exit $code
