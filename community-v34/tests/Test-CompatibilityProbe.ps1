[CmdletBinding()]
param([string]$ToolsRoot,[string]$WorkRoot,[switch]$IncludeRegisteredInventory)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
if(-not $PSBoundParameters.ContainsKey('ToolsRoot')){$ToolsRoot=Join-Path ([IO.Path]::GetDirectoryName([IO.Path]::GetDirectoryName($PSCommandPath))) 'packaging'}
if(-not $PSBoundParameters.ContainsKey('WorkRoot')){$WorkRoot=Join-Path ([IO.Path]::GetDirectoryName([IO.Path]::GetDirectoryName($PSCommandPath))) ('compatibility-tests\'+[Guid]::NewGuid().ToString('N'))}
. (Join-Path $ToolsRoot 'CompatibilityProbe.ps1')
if($PSVersionTable.PSVersion.Major -ne 5 -or $PSVersionTable.PSVersion.Minor -lt 1){throw 'TEST REQUIRES WINDOWS POWERSHELL 5.1'}
[void][IO.Directory]::CreateDirectory($WorkRoot)
$passed=0
function Check([bool]$Condition,[string]$Name){if(-not $Condition){throw ('TEST FAILED: '+$Name)};$script:passed++;Write-Output ('PASS '+$Name)}
function Save([string]$Path,[string]$Value){[void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($Path));[IO.File]::WriteAllText($Path,$Value,[Text.UTF8Encoding]::new($false))}
$name='OpenAI.Codex_26.1016.6573.0_x64__2p2nqsd0c76g0'
$xml='<Package xmlns="http://schemas.microsoft.com/appx/manifest/foundation/windows10"><Applications><Application Id="PRIVATE" Executable="app\Codex.exe"/><Application Id="SECRET" Executable="app\ChatGPT.exe"/></Applications></Package>'
Check (($null -eq (ConvertTo-CodexProbeVersion 'C:\Users\PRIVATE\1.2')) -and ($null -eq (ConvertTo-CodexProbeVersion '1.2 SECRET'))) 'version input cannot export private text'
Check (($null -eq (ConvertTo-CodexProbeVersion '4294967296.0')) -and ($null -eq (ConvertTo-CodexProbeVersion '1.2.3.4.5'))) 'oversized numeric and long version rejected'
Check (((ConvertTo-CodexProbeVersion '10.0.26100.0') -join ',') -ceq '10,0,26100,0') 'version exports numeric array'
foreach($invalid in @('Other.Codex_26.1016.6573.0_x64__2p2nqsd0c76g0','OpenAI.Codex_26.1.0.0_x64__attacker','OpenAI.Codex_C:\PRIVATE_x64__2p2nqsd0c76g0','OpenAI.Codex_9999999999.1.0.0_x64__2p2nqsd0c76g0',('x'*200))){Check ($null -eq (ConvertTo-CodexCompatibilityRecord $invalid @{} $xml)) 'unregistered-family or malformed identity rejected'}
$unknown=ConvertTo-CodexCompatibilityRecord $name @{} $null
Check ($unknown.runtimeEvidence -ceq 'unknown' -and $unknown.markers.appAsar -eq -1 -and $unknown.manifest.state -ceq 'unknown') 'absent observations remain unknown'
$electron=ConvertTo-CodexCompatibilityRecord $name @{chatgptExe=$true;appAsar=$true;chromeDll=$true;owlRuntimeJson=$true;owlElectronJson=$true} $xml
Check ($electron.runtimeEvidence -ceq 'electron-evidence' -and $electron.architecture -ceq 'x64' -and ($electron.version -join '.') -ceq '26.1016.6573.0') 'electron family is evidence not support guarantee'
Check (($electron.manifest.applicationRoles -join ',') -ceq 'codex-entry,chatgpt-main') 'manifest exports relative role enums only'
$wv=ConvertTo-CodexCompatibilityRecord $name @{webview2LoaderAtApp=$true;chatgptExe=$false;appAsar=$false} $xml
Check ($wv.runtimeEvidence -ceq 'webview2-evidence') 'WebView2 marker does not infer Tauri'
$mixed=ConvertTo-CodexCompatibilityRecord $name @{webview2LoaderAtApp=$true;chatgptExe=$true;appAsar=$true} $xml
Check ($mixed.runtimeEvidence -ceq 'mixed') 'both marker families are mixed'
$fake=ConvertTo-CodexCompatibilityRecord $name @{chatgptExe='True';appAsar=1;owlRuntimeJson='C:\PRIVATE';apiKey='SECRET'} $null 'C:\PRIVATE'
Check ($fake.runtimeEvidence -ceq 'unknown' -and $fake.markers.chatgptExe -eq -1 -and $fake.markers.appAsar -eq -1 -and $fake.readState -ceq 'unavailable') 'strict boolean evidence and bounded state'
$manifestCases=@{
    malformed='<Package>'
    privateAbsolute='<Package><Applications><Application Executable="C:\Users\PRIVATE\SECRET.exe"/></Applications></Package>'
    traversal='<Package><Applications><Application Executable="app\..\SECRET.exe"/></Applications></Package>'
    dtd='<!DOCTYPE Package [<!ENTITY private SYSTEM "file:///C:/PRIVATE">]><Package><Applications><Application Executable="&private;"/></Applications></Package>'
    other='<Package><Applications><Application Executable="app\PRIVATE-SECRET.exe"/></Applications></Package>'
    wrongRoot='<PRIVATE><Applications><Application Executable="app\ChatGPT.exe"/></Applications></PRIVATE>'
}
Check ((Get-CodexProbeManifest $manifestCases.malformed).state -ceq 'malformed') 'malformed XML bounded'
Check ((Get-CodexProbeManifest $manifestCases.dtd).state -ceq 'malformed') 'external DTD prohibited'
Check ((Get-CodexProbeManifest $manifestCases.wrongRoot).state -ceq 'malformed') 'manifest root required'
Check (((Get-CodexProbeManifest $manifestCases.privateAbsolute).applicationRoles -join ',') -ceq 'invalid') 'absolute executable rejected'
Check (((Get-CodexProbeManifest $manifestCases.traversal).applicationRoles -join ',') -ceq 'invalid') 'parent traversal rejected'
Check (((Get-CodexProbeManifest $manifestCases.other).applicationRoles -join ',') -ceq 'other-relative') 'unknown relative executable classified without exposure'
Check ((Get-CodexProbeManifest ('x'*65537)).state -ceq 'too-large') 'manifest character budget'
$many=Get-CodexProbeManifest ('<Package><Applications>'+('<Application Executable="app\ChatGPT.exe"/>'*25)+'</Applications></Package>')
Check ($many.truncated -and $many.applicationRoles.Count -eq 16) 'manifest application cap'
$caseJson=@($electron,$mixed,$fake,($manifestCases.Values|ForEach-Object {Get-CodexProbeManifest $_}))|ConvertTo-Json -Depth 8
Check ($caseJson -notmatch 'PRIVATE|SECRET|apiKey|C:|file:|Tauri|Wry') 'fixture JSON contains only allowlisted observations'
Check (-not (Test-CodexProbeLocalPath '\\server\PRIVATE\app')) 'UNC path rejected before filesystem read'
Check (-not (Test-CodexProbeLocalPath 'C:\PRIVATE:stream')) 'alternate data stream rejected'
Check (-not (Test-CodexProbeLocalPath 'C:\PRIVATE\..\Windows')) 'noncanonical traversal rejected'
$blocked=Read-CodexProbePackage $name '\\server\PRIVATE' ([Diagnostics.Stopwatch]::StartNew()) 1000
Check ($blocked.readState -ceq 'blocked-root' -and $blocked.runtimeEvidence -ceq 'unknown') 'blocked root returns bounded unknown evidence'
$fixture=Join-Path $WorkRoot '合成目录 空格'
Save (Join-Path $fixture 'app\ChatGPT.exe') 'fixture only; never execute'
Save (Join-Path $fixture 'app\resources\app.asar') 'fixture only'
Save (Join-Path $fixture 'AppxManifest.xml') $xml
$before=@{};Get-ChildItem -LiteralPath $fixture -Recurse -File|ForEach-Object {$before[$_.FullName]=(Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash}
$sample=Read-CodexProbePackage $name $fixture ([Diagnostics.Stopwatch]::StartNew()) 3000
Check ($sample.runtimeEvidence -ceq 'electron-evidence' -and $sample.readState -ceq 'ok' -and $sample.markers.webview2LoaderAtApp -eq 0) 'exact local file fixture with Unicode and spaces'
foreach($file in $before.Keys){Check ((Get-FileHash -LiteralPath $file -Algorithm SHA256).Hash -ceq $before[$file]) 'input bytes unchanged'}
$expired=Read-CodexProbePackage $name $fixture ([Diagnostics.Stopwatch]::StartNew()) 0
Check ($expired.readState -ceq 'budget-exhausted' -and $expired.runtimeEvidence -ceq 'unknown') 'expired budget skips marker and manifest reads'
$largePath=Join-Path $fixture 'AppxManifest.xml';Save $largePath ('x'*65537)
$large=Read-CodexProbePackage $name $fixture ([Diagnostics.Stopwatch]::StartNew()) 3000
Check ($large.manifest.state -ceq 'too-large') 'actual file byte budget'
$junction=Join-Path $WorkRoot 'junction-to-fixture'
$linkMade=$false
try{New-Item -ItemType Junction -Path $junction -Target $fixture -ErrorAction Stop|Out-Null;$linkMade=$true}catch{}
if($linkMade){
    Check (-not (Test-CodexProbeLocalPath (Join-Path $junction 'app\ChatGPT.exe'))) 'ancestor reparse point rejected'
    $linked=Read-CodexProbePackage $name $junction ([Diagnostics.Stopwatch]::StartNew()) 3000
    Check ($linked.readState -ceq 'blocked-root' -and $linked.markers.chatgptExe -eq -1) 'linked package is not followed'
}else{Write-Output 'SKIP junction fixture unsupported; no elevation requested'}
$coldMs=$null;$warmMs=$null;$inventory=$null
if($IncludeRegisteredInventory){
    $inventory=Get-CodexCompatibilityInventory -BudgetMilliseconds 3000;$coldMs=$inventory.elapsedMs
    $warm=Get-CodexCompatibilityInventory -BudgetMilliseconds 1500;$warmMs=$warm.elapsedMs
    Check ($inventory.kind -ceq 'CodexCompatibilityEvidence' -and $inventory.schemaVersion -eq 1 -and -not $inventory.transportSupportVerified) 'actual inventory does not claim transport support'
    Check ($inventory.registeredPackageCount -le 32 -and $inventory.packages.Count -le 32) 'native registration result capped'
    $json=$inventory|ConvertTo-Json -Depth 8
    Check ($json -notmatch 'Administrator|Users\\|WindowsApps|OpenAI\.Codex_|PRIVATE|SECRET|Tauri|Wry') 'native inventory excludes raw package names and paths'
    Check (-not $warm.budgetExhausted) 'warm bounded probe completes'
    [IO.File]::WriteAllText((Join-Path $WorkRoot 'sanitized-inventory.json'),$json,[Text.UTF8Encoding]::new($true))
}
$source=Join-Path $ToolsRoot 'CompatibilityProbe.ps1'
$receipt=[ordered]@{passed=$passed;powerShell=$PSVersionTable.PSVersion.ToString();sourceSha256=(Get-FileHash -LiteralPath $source -Algorithm SHA256).Hash;testSha256=(Get-FileHash -LiteralPath $PSCommandPath -Algorithm SHA256).Hash;coldInventoryMs=$coldMs;warmInventoryMs=$warmMs;junctionFixture=$linkMade}
[IO.File]::WriteAllText((Join-Path $WorkRoot 'results.json'),($receipt|ConvertTo-Json),[Text.UTF8Encoding]::new($true))
Write-Output ('PASS compatibility probe checks='+$passed+'; coldMs='+$coldMs+'; warmMs='+$warmMs)
