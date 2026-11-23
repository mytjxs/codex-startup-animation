[CmdletBinding()]
param([string]$ReleaseRoot)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
try {
    # Windows PowerShell 5.1 needs advanced-script defaults resolved in the body.
    $toolsDirectory=[IO.Path]::GetDirectoryName($PSCommandPath)
    if(-not $PSBoundParameters.ContainsKey('ReleaseRoot')){$ReleaseRoot=$toolsDirectory}
    $helper=[IO.Path]::Combine($toolsDirectory,'DiagnosticReport.Common.ps1')
    if($helper -notmatch '^[A-Za-z]:\\' -or $helper.Substring(2).Contains(':')){throw 'FCP-UNSAFE-PATH'}
    if(([IO.DriveInfo]::new([IO.Path]::GetPathRoot($helper))).DriveType -eq [IO.DriveType]::Network){throw 'FCP-NETWORK-DRIVE'}
    for($cursor=$helper;$cursor;$cursor=[IO.Path]::GetDirectoryName($cursor)){
        if(([IO.File]::GetAttributes($cursor) -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'FCP-REPARSE-PATH'}
    }
    . $helper
    Assert-FixPowerShell
    $report=Write-PortableDiagnosticReport -ReleaseRoot $ReleaseRoot
    Write-Host 'PASS: Bounded diagnostic report created. No raw logs or private paths included.'
    Write-Host ('Report ZIP: '+$report)
    Write-Host 'The manifest identifies this release; this report does not claim a full file-integrity check.'
    Write-Host 'No application was launched, stopped, or waited for.'
} catch {
    $code='FCP-IO-OR-FORMAT'
    if($_.Exception.Message -cmatch '^FCP-[A-Z0-9-]+$'){$code=$_.Exception.Message}
    Write-Host ('FAILED: '+$code+'. No raw logs were exported.')
    exit 1
}
