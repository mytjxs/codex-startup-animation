param(
    [string]$Source,
    [ValidateSet('baseline','fixed')][string]$Mode='fixed',
    [string]$Node='node',
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
$assembly=Join-Path $outputRoot 'StartupHandoffTests.exe'
$projectRoot=Split-Path -Parent ([IO.Path]::GetFullPath($Source))
if($outputRoot.StartsWith($projectRoot+[IO.Path]::DirectorySeparatorChar,[StringComparison]::OrdinalIgnoreCase)){throw 'Test artifacts must be outside the source project'}
$contractFiles=@((Join-Path $projectRoot 'src\BridgeActivation.cs'),(Join-Path $projectRoot 'src\StartupReliability.cs'))
function Read-ActualClass([string]$Path,[string]$Name){
    $sourceText=[IO.File]::ReadAllText($Path)
    $match=[regex]::Match($sourceText,'(?ms)^    internal sealed class '+[regex]::Escape($Name)+'\b.*?^    }')
    if(-not $match.Success){throw ('Missing actual class '+$Name)}
    return $match.Value
}
$contract=Join-Path $outputRoot 'ActualPreparationContract.cs'
$contractText="using System;`r`nusing System.Runtime.Serialization;`r`nnamespace DragonLauncher {`r`n"+(Read-ActualClass $contractFiles[0] 'BridgeStatus')+"`r`n"+(Read-ActualClass $contractFiles[1] 'StartupPreparation')+"`r`n}`r`n"
[IO.File]::WriteAllText($contract,$contractText,[Text.UTF8Encoding]::new($false))
$metadata=Join-Path $projectRoot 'build-inputs\RuntimeMetadata.cs'
$manifest=Join-Path $projectRoot 'build-inputs\Runtime.manifest'
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if(-not (Test-Path -LiteralPath $csc)){$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe'}
& $csc /nologo /target:exe /platform:x64 /main:CodexDragonWallpaper.StartupHandoffTests /r:System.Web.Extensions.dll /r:System.Runtime.Serialization.dll ('/win32manifest:'+$manifest) ('/out:'+$assembly) @files $metadata $contract (Join-Path $scriptDirectory 'StartupHandoffTests.cs')
if($LASTEXITCODE -ne 0){throw 'Compile failed'}
$hashes=@(foreach($file in @($files)+$contractFiles){Get-FileHash -LiteralPath $file -Algorithm SHA256 | Select-Object Path,Hash})
[IO.File]::WriteAllText((Join-Path $outputRoot 'source-hashes.json'),($hashes | ConvertTo-Json),[Text.UTF8Encoding]::new($false))
$result=& $assembly (Join-Path $outputRoot 'fixtures') $Mode
$code=$LASTEXITCODE
[IO.File]::WriteAllText((Join-Path $outputRoot 'results.json'),($result -join [Environment]::NewLine),[Text.UTF8Encoding]::new($false))
$result
if($code -ne 0){exit $code}
if($Mode -eq 'fixed'){
    $nodeResult=& $Node (Join-Path $scriptDirectory 'Test-LeasedOperation.js') (Join-Path $projectRoot 'renderer.js') (Join-Path $outputRoot 'fixtures\leased-operation.js')
    $code=$LASTEXITCODE
    [IO.File]::WriteAllText((Join-Path $outputRoot 'renderer-results.json'),($nodeResult -join [Environment]::NewLine),[Text.UTF8Encoding]::new($false))
    $nodeResult
}
$build=[ordered]@{powerShellVersion=$PSVersionTable.PSVersion.ToString();targetFramework='.NETFramework,Version=v4.8';platform='x64';manifestSha256=(Get-FileHash -LiteralPath $manifest -Algorithm SHA256).Hash;metadataSha256=(Get-FileHash -LiteralPath $metadata -Algorithm SHA256).Hash;productStarted=$false}
[IO.File]::WriteAllText((Join-Path $outputRoot 'build-metadata.json'),($build|ConvertTo-Json),[Text.UTF8Encoding]::new($false))
exit $code
