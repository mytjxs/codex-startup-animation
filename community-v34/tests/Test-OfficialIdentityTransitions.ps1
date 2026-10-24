param([Parameter(Mandatory=$true)][string]$OutputDirectory)
$ErrorActionPreference='Stop'
$source=Split-Path -Parent $PSScriptRoot
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Choose a fresh isolated output directory.'}
if($output.StartsWith($source+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Test output must be outside source.'}
[void][IO.Directory]::CreateDirectory($output)
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$exe=Join-Path $output 'OfficialIdentityTransitionTests.exe'
$identity=Join-Path $source 'src/OfficialIdentity.cs'
& (Join-Path $framework 'csc.exe') /nologo /target:exe /platform:x64 /optimize+ /codepage:65001 /warn:4 /warnaserror+ ('/out:'+$exe) ('/r:'+(Join-Path $framework 'System.Xml.dll')) $identity (Join-Path $PSScriptRoot 'OfficialIdentityTransitionTests.cs')
if($LASTEXITCODE -ne 0){throw 'Identity transition fixture compilation failed.'}
$result=@(& $exe (Join-Path $output 'fixtures'));$code=$LASTEXITCODE
$result|Set-Content -LiteralPath (Join-Path $output 'results.txt') -Encoding UTF8
Get-FileHash -LiteralPath $identity -Algorithm SHA256|Select-Object Hash|ConvertTo-Json|Set-Content -LiteralPath (Join-Path $output 'source-hash.json') -Encoding UTF8
$result
exit $code
