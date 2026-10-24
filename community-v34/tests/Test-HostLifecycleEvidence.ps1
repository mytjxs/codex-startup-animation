param(
    [string]$Source,
    [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference='Stop'
$scriptDirectory=Split-Path -Parent $PSCommandPath
if([string]::IsNullOrWhiteSpace($Source)){$Source=Join-Path (Split-Path -Parent $scriptDirectory) 'wallpaper-host-src'}
$files=Get-ChildItem -LiteralPath $Source -Filter '*.cs' | ForEach-Object FullName
if($files.Count -ne 5){throw 'Expected five host source files'}
$files=@($files)+@(Join-Path (Split-Path -Parent ([IO.Path]::GetFullPath($Source))) 'src\OfficialIdentity.cs')
[void][IO.Directory]::CreateDirectory($OutputDirectory)
$outputRoot=[IO.Path]::GetFullPath($OutputDirectory)
$assembly=Join-Path $outputRoot 'HostLifecycleEvidenceTests.exe'
$projectRoot=Split-Path -Parent $scriptDirectory
$metadata=Join-Path $projectRoot 'build-inputs\RuntimeMetadata.cs'
$manifest=Join-Path $projectRoot 'build-inputs\Runtime.manifest'
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if(-not (Test-Path -LiteralPath $csc)){$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'}
& $csc /nologo /target:exe /platform:x64 /main:CodexDragonWallpaper.HostLifecycleEvidenceTests /r:System.Web.Extensions.dll ('/win32manifest:'+$manifest) ('/out:'+$assembly) @files $metadata (Join-Path $scriptDirectory 'HostLifecycleEvidenceTests.cs')
if($LASTEXITCODE -ne 0){throw 'Compile failed'}
$hashes=@(foreach($file in $files){Get-FileHash -LiteralPath $file -Algorithm SHA256 | Select-Object Path,Hash})
[IO.File]::WriteAllText((Join-Path $outputRoot 'source-hashes.json'),($hashes | ConvertTo-Json),[Text.UTF8Encoding]::new($false))
$result=& $assembly (Join-Path $outputRoot 'fixtures')
$code=$LASTEXITCODE
[IO.File]::WriteAllText((Join-Path $outputRoot 'results.json'),($result -join [Environment]::NewLine),[Text.UTF8Encoding]::new($false))
$result
exit $code