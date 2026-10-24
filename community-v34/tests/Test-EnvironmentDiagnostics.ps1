[CmdletBinding()]
param([string]$ToolsRoot,[string]$WorkRoot,[switch]$IncludeLocalInventory)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
if(-not $PSBoundParameters.ContainsKey('ToolsRoot')){$ToolsRoot=Join-Path ([IO.Path]::GetDirectoryName([IO.Path]::GetDirectoryName($PSCommandPath))) 'packaging'}
if(-not $PSBoundParameters.ContainsKey('WorkRoot')){throw 'Provide WorkRoot outside the source directory'}
. (Join-Path $ToolsRoot 'EnvironmentDiagnostics.ps1')
if($PSVersionTable.PSVersion.Major -ne 5 -or $PSVersionTable.PSVersion.Minor -lt 1){throw 'TEST REQUIRES WINDOWS POWERSHELL 5.1'}
[void][IO.Directory]::CreateDirectory($WorkRoot)
$passed=0
function Check([bool]$Condition,[string]$Name){if(-not $Condition){throw ('TEST FAILED: '+$Name)};$script:passed++;Write-Output ('PASS '+$Name)}
$unknown=ConvertTo-CodexEnvironmentEvidence @{}
Check ($unknown.kind -ceq 'CodexEnvironmentEvidence' -and $unknown.schemaVersion -eq 1) 'fixed schema'
Check ($unknown.windowsVersion.Count -eq 0 -and $unknown.osArchitecture -ceq 'unknown' -and $unknown.logicalProcessors -eq -1) 'unknown does not become zero success'
Check (-not $unknown.privileges.elevationKnown -and -not $unknown.privileges.elevated) 'unknown elevation distinguished'
foreach($value in @('PRIVATE','1.2 SECRET','1.2.3.4.5','4294967296.1','C:\PRIVATE\1.2')){Check ((ConvertTo-CodexEnvironmentVersion $value).Count -eq 0) 'version privacy and range boundary'}
Check (((ConvertTo-CodexEnvironmentVersion '10.0.26100.0') -join ',') -ceq '10,0,26100,0') 'version numeric projection'
foreach($value in @('PRIVATE','2/30/2026','13/1/2026','1/1/1800','1/1/2200','2026-01-01 SECRET',('x'*500))){Check ((ConvertTo-CodexEnvironmentDate $value).Count -eq 0) 'date privacy and calendar boundary'}
Check (((ConvertTo-CodexEnvironmentDate '2/29/2024') -join ',') -ceq '2024,2,29') 'valid leap date'
Check (((ConvertTo-CodexEnvironmentDate '2026-10-22') -join ',') -ceq '2026,10,22') 'ISO date numeric projection'
$vendors=@{'NVIDIA'='nvidia';'NVIDIA Corporation'='nvidia';'Advanced Micro Devices, Inc.'='amd';'Intel Corporation'='intel';'Microsoft'='microsoft';'NVIDIA SECRET'='unknown';'PRIVATE'='unknown'}
foreach($name in $vendors.Keys){$gpu=ConvertTo-CodexEnvironmentGpu @{provider=$name;version='32.0.15.9999';date='10/22/2026';serial='PRIVATE'};Check ($gpu.vendor -ceq $vendors[$name]) 'strict vendor enum';Check (@($gpu.PSObject.Properties).Count -eq 3) 'GPU schema contains only vendor and numeric versions'}
$data=@{windowsVersion='10.0.26100.0';osArchitecture='x64';processArchitecture='x86';logicalProcessors=32;dotNetFrameworkRelease=533509;totalPhysicalBytes=34359738368L;availablePhysicalBytes=17179869184L;totalCommitBytes=51539607552L;availableCommitBytes=21474836480L;monitorCount=2;primaryWidth=2560;primaryHeight=1440;virtualWidth=4480;virtualHeight=1440;systemDpi=144;userLogPixels=144;machineLogPixels=96;dpiAwareness='system';gpuDrivers=@(@{provider='NVIDIA';version='32.0.15.9999';date='10/22/2026'});gpuState='registry-evidence';gpuScannedNodes=32;gpuCapped=$false;elevated=$true;budgetMs=1500;elapsedMs=50;budgetExhausted=$false;readFailures=0;apiKey='PRIVATE';userName='SECRET'}
$valid=ConvertTo-CodexEnvironmentEvidence $data
Check ($valid.logicalProcessors -eq 32 -and $valid.memory.totalPhysicalBytes -eq 34359738368L) 'bounded CPU and memory numbers'
Check ($valid.display.monitorCount -eq 2 -and $valid.display.systemDpi -eq 144 -and $valid.display.metricSpace -ceq 'process-coordinate-space') 'dimensions and DPI are explicit coordinate-space evidence'
Check ($valid.privileges.elevationKnown -and $valid.privileges.elevated) 'strict elevation bool'
Check ($valid.gpu.drivers.Count -eq 1 -and $valid.gpu.evidence -ceq 'registered-driver-not-active-load') 'GPU evidence does not claim active rendering device'
$arm=ConvertTo-CodexEnvironmentEvidence @{osArchitecture='arm64';processArchitecture='arm64';primaryScalePercent=125};Check ($arm.osArchitecture -ceq 'arm64' -and $arm.processArchitecture -ceq 'arm64' -and $arm.display.primaryScalePercent -eq 125) 'ARM64 and physical primary scaling preserved'
$bad=@{windowsVersion='PRIVATE';osArchitecture='PRIVATE';processArchitecture='ARM64 PRIVATE';logicalProcessors='32';dotNetFrameworkRelease=-5;totalPhysicalBytes=[uint64]::MaxValue;availablePhysicalBytes=[double]::NaN;monitorCount=129;primaryWidth=131073;systemDpi=961;gpuDrivers=@(@{provider='SECRET';version='PRIVATE';date='SECRET'});gpuState='PRIVATE';gpuScannedNodes=33;gpuCapped='true';elevated=1;budgetMs=2001;readFailures=129;elapsedMs='50';budgetExhausted='true'}
$rejected=ConvertTo-CodexEnvironmentEvidence $bad
Check ($rejected.logicalProcessors -eq -1 -and $rejected.dotNetFrameworkRelease -eq -1 -and $rejected.memory.totalPhysicalBytes -eq -1 -and $rejected.memory.availablePhysicalBytes -eq -1) 'numbers reject strings overflow NaN and negative values'
Check ($rejected.display.monitorCount -eq -1 -and $rejected.display.primaryWidth -eq -1 -and $rejected.display.systemDpi -eq -1) 'display caps'
Check (-not $rejected.privileges.elevationKnown -and -not $rejected.gpu.capped -and -not $rejected.budgetExhausted) 'truthy strings and numbers cannot forge booleans'
Check ($rejected.gpu.state -ceq 'unknown' -and $rejected.gpu.scannedNodes -eq -1) 'GPU state and node cap'
$many=ConvertTo-CodexEnvironmentEvidence @{gpuDrivers=(@{provider='NVIDIA';version='1.2';date='1/1/2026'},@{provider='Intel'})*10}
Check ($many.gpu.drivers.Count -eq 8) 'GPU projection caps driver rows'
$json=@($unknown,$valid,$rejected,$many)|ConvertTo-Json -Depth 10
Check ($json -notmatch 'PRIVATE|SECRET|apiKey|userName|C:|serial|provider') 'private fixture strings and extra fields absent'
$expired=Get-CodexEnvironmentEvidence -BudgetMilliseconds 0
Check ($expired.budgetExhausted -and $expired.gpu.scannedNodes -eq 0 -and $expired.logicalProcessors -eq -1) 'expired budget performs no inventory'
$cold=$null;$warm=$null
if($IncludeLocalInventory){
    $cold=Get-CodexEnvironmentEvidence -BudgetMilliseconds 2000;$warm=Get-CodexEnvironmentEvidence -BudgetMilliseconds 1500
    Check ($cold.elapsedMs -lt 2000 -and -not $cold.budgetExhausted) 'cold bounded local facts complete within 2 seconds'
    Check ($warm.elapsedMs -lt 1500 -and -not $warm.budgetExhausted) 'warm bounded local facts complete within 1.5 seconds'
    Check ($cold.memory.totalPhysicalBytes -gt 0 -and $cold.logicalProcessors -gt 0 -and $cold.display.monitorCount -gt 0) 'local native observations exist'
    Check ($cold.display.primaryScalePercent -ge 100 -and $cold.display.primaryScalePercent -le 500) 'primary monitor scaling available independently of process DPI awareness'
    Check ($cold.gpu.scannedNodes -le 32 -and $cold.gpu.drivers.Count -le 8) 'local GPU node and row caps'
    $localJson=$cold|ConvertTo-Json -Depth 8
    Check ($localJson -notmatch 'Administrator|Users\\|WindowsApps|PRIVATE|SECRET|VEN_|DEV_|PCI\\|serial|DeviceID|username') 'local output excludes identifiers paths and free-form device names'
    [IO.File]::WriteAllText((Join-Path $WorkRoot 'sanitized-environment.json'),$localJson,[Text.UTF8Encoding]::new($true))
}
$source=Join-Path $ToolsRoot 'EnvironmentDiagnostics.ps1';$text=[IO.File]::ReadAllText($source)
Check ($text -notmatch '(?im)^\s*(?:Get-CimInstance|Get-WmiObject|Start-Job|Start-Process|Invoke-WebRequest|Invoke-RestMethod|Add-Type)\b') 'collector uses no process WMI job network or compiler commands'
Check ($text -notmatch '(?i)GetSubKeyNames\(|GetValueNames\(|Win32_VideoController|Win32_Processor|CommandLine|HardwareID|MatchingDeviceID|DriverDesc|SerialNumber') 'collector avoids enumeration and identifier sources'
$bytes=[IO.File]::ReadAllBytes($source);Check ($bytes.Length -ge 3 -and $bytes[0] -eq 239 -and $bytes[1] -eq 187 -and $bytes[2] -eq 191) 'module is UTF8 BOM for PowerShell 5.1'
$receipt=[ordered]@{passed=$passed;sourceSha256=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash;testSha256=(Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash;coldMs=if($cold){$cold.elapsedMs}else{-1};warmMs=if($warm){$warm.elapsedMs}else{-1}}
[IO.File]::WriteAllText((Join-Path $WorkRoot 'results.json'),($receipt|ConvertTo-Json),[Text.UTF8Encoding]::new($true))
Write-Output ('PASS environment checks='+$passed+'; coldMs='+$receipt.coldMs+'; warmMs='+$receipt.warmMs)
