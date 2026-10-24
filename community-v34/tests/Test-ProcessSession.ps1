param(
    [string]$Source,
    [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference='Stop'
Set-StrictMode -Version Latest
$scriptDirectory=Split-Path -Parent $PSCommandPath
$projectRoot=Split-Path -Parent $scriptDirectory
if([string]::IsNullOrWhiteSpace($Source)){$Source=Join-Path $projectRoot 'wallpaper-host-src\Program.cs'}
$sourceText=[IO.File]::ReadAllText($Source,[Text.Encoding]::UTF8)
$pattern='(?s)    internal static bool Existing\(string exe,out int pid,out long created\)\{.*?(?=    static Mutex LaunchLock\()'
$methods=[regex]::Matches($sourceText,$pattern)
if($methods.Count -ne 1){throw 'Production Existing method shape changed; update extraction explicitly.'}
$method=$methods[0].Value
$template=[IO.File]::ReadAllText((Join-Path $scriptDirectory 'Test-ProcessSession.cs'),[Text.Encoding]::UTF8)
$placeholder='/* INSERT_EXACT_EXISTING_METHOD */'
if(([regex]::Matches($template,[regex]::Escape($placeholder))).Count -ne 1){throw 'Expected one production method insertion marker.'}
[void][IO.Directory]::CreateDirectory($OutputDirectory)
$outputRoot=[IO.Path]::GetFullPath($OutputDirectory)
$generated=Join-Path $outputRoot 'ProcessSessionFixture.cs'
$assembly=Join-Path $outputRoot 'ProcessSessionFixture.exe'
[IO.File]::WriteAllText($generated,$template.Replace($placeholder,$method),[Text.UTF8Encoding]::new($false))
$csc=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$metadata=Join-Path $projectRoot 'build-inputs\RuntimeMetadata.cs'
$manifest=Join-Path $projectRoot 'build-inputs\Runtime.manifest'
& $csc /nologo /warnaserror /target:exe /platform:x64 /main:CodexProcessSessionTests.ProcessSessionTests ('/win32manifest:'+$manifest) ('/out:'+$assembly) $generated $metadata
if($LASTEXITCODE -ne 0){throw 'Compile failed'}
$receipt=[ordered]@{sourceSha256=(Get-FileHash -LiteralPath $Source -Algorithm SHA256).Hash;scope='Exact production method; synthetic processes only. No real multi-session OS test.'}
[IO.File]::WriteAllText((Join-Path $outputRoot 'source-receipt.json'),($receipt|ConvertTo-Json),[Text.UTF8Encoding]::new($false))
$result=& $assembly
$code=$LASTEXITCODE
[IO.File]::WriteAllText((Join-Path $outputRoot 'results.json'),($result -join [Environment]::NewLine),[Text.UTF8Encoding]::new($false))
$result
exit $code
