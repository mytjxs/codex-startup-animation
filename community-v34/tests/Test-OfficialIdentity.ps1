[CmdletBinding()]
param([string]$SourceRoot,[Parameter(Mandatory=$true)][string]$OutputDirectory,[switch]$ReadOnlyLive)
$ErrorActionPreference='Stop'
if([string]::IsNullOrWhiteSpace($SourceRoot)){$SourceRoot=Split-Path -Parent $PSScriptRoot}
$source=[IO.Path]::GetFullPath($SourceRoot)
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Choose a new empty test output path.'}
[void][IO.Directory]::CreateDirectory($output)
$fixture=Join-Path $output 'official-identity-fixtures'
$target=Join-Path $fixture 'new'
[void][IO.Directory]::CreateDirectory($target)
# This reparse fixture and its target are both inside the fresh test directory.
# It never points at application installations or user configuration.
[void](New-Item -ItemType Junction -Path (Join-Path $fixture 'linked-package') -Target $target)
$framework=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319'
$compiler=Join-Path $framework 'csc.exe'
$executable=Join-Path $output 'OfficialIdentityTests.exe'
$arguments=@('/nologo','/target:exe','/platform:x64','/optimize+','/codepage:65001','/warn:4','/warnaserror+',('/out:'+$executable),('/r:'+(Join-Path $framework 'System.Xml.dll')),(Join-Path $source 'src/OfficialIdentity.cs'),(Join-Path $PSScriptRoot 'OfficialIdentityTests.cs'))
& $compiler @arguments
if($LASTEXITCODE -ne 0){throw 'Official identity contract compilation failed.'}
& $executable $fixture
if($LASTEXITCODE -ne 0){throw 'Official identity contracts failed.'}
if($ReadOnlyLive){
    & $executable '--read-only-live'
    if($LASTEXITCODE -ne 0){throw 'Official identity live read-only validation failed.'}
}
