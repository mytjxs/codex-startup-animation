param([Parameter(Mandatory=$true)][string]$OutputDirectory)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '..\packaging\OfficialShortcut.ps1')
$output=Assert-ShortcutLocalPath $OutputDirectory
if(Test-Path -LiteralPath $output){throw 'Choose a fresh isolated fixture directory.'}
[void][IO.Directory]::CreateDirectory($output)
$script:packages=@();$script:signatures=@{};$script:signatureReads=0
$checks=[Collections.Generic.List[object]]::new()
function Check([bool]$Condition,[string]$Name){$checks.Add([pscustomobject]@{name=$Name;passed=$Condition})}
# Replace only discovery providers in this test process. No actual installed
# package, signature, shortcut or official application is queried or modified.
function Get-AppxPackage {param($Name,$ErrorAction) return $script:packages}
function Get-AuthenticodeSignature {
    param($LiteralPath)
    if(-not $LiteralPath.StartsWith($output+'\',[StringComparison]::OrdinalIgnoreCase)){throw 'Fixture escaped its isolated root.'}
    $script:signatureReads++
    if(-not $script:signatures.ContainsKey($LiteralPath)){throw 'Unexpected signature request.'}
    $record=$script:signatures[$LiteralPath]
    $certificate=[pscustomobject]@{Name=$record.signer}
    $certificate|Add-Member -MemberType ScriptMethod -Name GetNameInfo -Value {param($Kind,$Issuer) return $this.Name}
    return [pscustomobject]@{Status=$record.status;SignerCertificate=$certificate}
}
function Manifest([string]$Version,[string]$Executable,[string]$Extra=''){
    return "<Package><Identity Name='OpenAI.Codex' ProcessorArchitecture='x64' Version='$Version'/><Applications><Application Id='App' Executable='$Executable'/>$Extra</Applications></Package>"
}
function Package([string]$Name,[string]$Version,[string]$Relative='app/ChatGPT.exe',[string]$Signer='OpenAI OpCo, LLC'){
    $root=Join-Path $output $Name
    [void][IO.Directory]::CreateDirectory($root)
    $exe=Join-Path $root $Relative
    [void][IO.Directory]::CreateDirectory([IO.Path]::GetDirectoryName($exe))
    [IO.File]::WriteAllBytes($exe,[byte[]]@(1,2,3))
    [IO.File]::WriteAllText((Join-Path $root 'AppxManifest.xml'),(Manifest $Version $Relative),[Text.UTF8Encoding]::new($false))
    $script:signatures[$exe]=@{status='Valid';signer=$Signer}
    return [pscustomobject]@{PackageFamilyName='OpenAI.Codex_2p2nqsd0c76g0';PackageFullName=('OpenAI.Codex_'+$Version+'_x64__2p2nqsd0c76g0');InstallLocation=$root;FixtureExecutable=$exe}
}
function Discover {
    try{return [pscustomobject]@{ok=$true;value=(Get-RegisteredOfficialShortcutIdentity)}}catch{return [pscustomobject]@{ok=$false;value=$null}}
}
function Contains-Executable($Result,[string]$Path){return $Result.ok -and @($Result.value.Executables).Contains($Path)}
function Rejected-Manifest($Package,[string]$Xml,[string]$Name){
    [IO.File]::WriteAllText((Join-Path $Package.InstallLocation 'AppxManifest.xml'),$Xml,[Text.UTF8Encoding]::new($false))
    $script:packages=@($Package);$before=$script:signatureReads;$found=Discover
    Check ((-not $found.ok) -or @($found.value.AppIds).Count -eq 0) $Name
    Check ($script:signatureReads -eq $before) ($Name+' rejected before executable signature read')
}
$renamed=Package 'renamed' '26.2001.1.0' 'ui/CodexDesktop.exe';$script:packages=@($renamed);$found=Discover
Check (Contains-Executable $found $renamed.FixtureExecutable) 'renamed manifest GUI executable recognized without hardcoded filename'
Check ($found.ok -and @($found.value.AppIds).Count -eq 1 -and $found.value.AppIds[0] -ceq 'OpenAI.Codex_2p2nqsd0c76g0!App') 'only verified manifest App identity exported'
$old=Package 'old' '26.1016.1.0';$new=Package 'new' '26.2001.2.0' 'different/CodexDesktop.exe'
foreach($order in @(@($old,$new),@($new,$old))){$script:packages=$order;$found=Discover;Check ((Contains-Executable $found $old.FixtureExecutable) -and (Contains-Executable $found $new.FixtureExecutable)) 'multiple registered versions keep independently verified direct links';Check ($found.ok -and @($found.value.AppIds).Count -eq 1) 'shared stable AppID is deduplicated'}
$script:packages=@($new);$found=Discover;Check ((Contains-Executable $found $new.FixtureExecutable) -and @($found.value.Executables).Count -eq 1) 'next discovery uses current registration and path without cached old version'
$moveRoot=Join-Path $output 'new-location';[void][IO.Directory]::CreateDirectory($moveRoot)
[void][IO.Directory]::CreateDirectory((Join-Path $moveRoot 'different'));[IO.File]::Copy((Join-Path $new.InstallLocation 'AppxManifest.xml'),(Join-Path $moveRoot 'AppxManifest.xml'))
$movedExe=Join-Path $moveRoot 'different/CodexDesktop.exe';[IO.File]::Copy($new.FixtureExecutable,$movedExe);$script:signatures[$movedExe]=@{status='Valid';signer='OpenAI OpCo, LLC'}
$new.InstallLocation=$moveRoot;$script:packages=@($new);$found=Discover;Check ((Contains-Executable $found $movedExe) -and @($found.value.Executables).Count -eq 1) 'changed registered root is reread rather than pinning old path'
foreach($signer in @('OpenAI Evil Company','OpenAI, Impostor','Other Corporation')){
    $item=Package ('signer-'+$script:signatureReads) '26.2001.3.0' 'app/ChatGPT.exe' $signer;$script:packages=@($item);$found=Discover
    Check ((-not $found.ok) -or @($found.value.Executables).Count -eq 0) 'valid same-prefix or foreign publisher cannot authorize executable'
    Check ((-not $found.ok) -or @($found.value.AppIds).Count -eq 0) 'untrusted signature cannot authorize namespace shortcut either'
}
foreach($signer in @('OpenAI','OpenAI, L.L.C.','OpenAI OpCo, LLC')){
    $item=Package ('valid-signer-'+$script:signatureReads) '26.2001.4.0' 'app/ChatGPT.exe' $signer;$script:packages=@($item);$found=Discover
    Check (Contains-Executable $found $item.FixtureExecutable) 'exact core publisher allowlist remains supported'
}
$bad=Package 'invalid-signature' '26.2001.5.0';$script:signatures[$bad.FixtureExecutable].status='NotTrusted';$script:packages=@($bad);$found=Discover
Check ((-not $found.ok) -or (@($found.value.Executables).Count -eq 0 -and @($found.value.AppIds).Count -eq 0)) 'invalid Authenticode signature cannot authorize any shortcut'
$unsafe=Package 'unsafe-manifest' '26.2001.6.0'
foreach($relative in @('../outside.exe','app/../outside.exe','C:/outside.exe','app/ChatGPT.exe:stream','app//ChatGPT.exe','app/ChatGPT.exe.')){Rejected-Manifest $unsafe (Manifest '26.2001.6.0' $relative) 'unsafe manifest path rejected'}
Rejected-Manifest $unsafe (Manifest '26.2001.5.0' 'app/ChatGPT.exe') 'manifest and registered version mismatch rejected'
Rejected-Manifest $unsafe ((Manifest '26.2001.6.0' 'app/ChatGPT.exe').Replace("Name='OpenAI.Codex'","Name='Other.App'")) 'foreign manifest identity rejected'
Rejected-Manifest $unsafe ((Manifest '26.2001.6.0' 'app/ChatGPT.exe').Replace("ProcessorArchitecture='x64'","ProcessorArchitecture='arm64'")) 'wrong architecture rejected'
Rejected-Manifest $unsafe (Manifest '26.2001.6.0' 'app/ChatGPT.exe' "<Application Id='App' Executable='app/ChatGPT.exe'/>") 'duplicate App executable rejected'
Rejected-Manifest $unsafe '<!DOCTYPE Package [<!ENTITY x SYSTEM "file:///C:/unrelated">]><Package>&x;</Package>' 'external XML entities rejected'
$missing=Package 'missing-executable' '26.2001.7.0';[IO.File]::Delete($missing.FixtureExecutable);$script:packages=@($missing);$found=Discover
Check ((-not $found.ok) -or (@($found.value.Executables).Count -eq 0 -and @($found.value.AppIds).Count -eq 0)) 'incomplete update registration cannot authorize a missing executable'
$extra=Package 'utility-app' '26.2001.8.0';[IO.File]::WriteAllText((Join-Path $extra.InstallLocation 'AppxManifest.xml'),(Manifest '26.2001.8.0' 'app/ChatGPT.exe' "<Application Id='Utility' Executable='app/Utility.exe'/>"));$script:packages=@($extra);$found=Discover
Check ($found.ok -and @($found.value.AppIds).Count -eq 1 -and $found.value.AppIds[0] -ceq 'OpenAI.Codex_2p2nqsd0c76g0!App') 'utility applications are not accepted as official GUI shortcut'
$script:packages=@($old,$old);$found=Discover;Check ((-not $found.ok) -or (@($found.value.Executables).Count -eq 1 -and @($found.value.AppIds).Count -eq 1)) 'duplicate inventory cannot duplicate shortcut identity'
$script:packages=@();$found=Discover;Check ($found.ok -and @($found.value.AppIds).Count -eq 0 -and @($found.value.Executables).Count -eq 0) 'empty registration returns no shortcut candidates'
$script:packages=@(1..33|ForEach-Object {$old});$before=$script:signatureReads;$found=Discover
Check (-not $found.ok -and $script:signatureReads -eq $before) 'package inventory limit rejects before signature work'
foreach($name in @('OpenAI.Codex_26.1.1.0_arm64__2p2nqsd0c76g0','OpenAI.Codex_26.1.1.0_x64__foreign','OpenAI.Codex_999999.1.1.0_x64__2p2nqsd0c76g0',('x'*241))){
    $item=[pscustomobject]@{PackageFamilyName='OpenAI.Codex_2p2nqsd0c76g0';PackageFullName=$name;InstallLocation=$old.InstallLocation};$script:packages=@($item);$before=$script:signatureReads;$found=Discover
    Check ($found.ok -and @($found.value.AppIds).Count -eq 0 -and @($found.value.Executables).Count -eq 0 -and $script:signatureReads -eq $before) 'invalid package name or architecture rejected before file trust checks'
}
$junction=Join-Path $output 'linked-package';[void](New-Item -ItemType Junction -Path $junction -Target $old.InstallLocation)
$item=[pscustomobject]@{PackageFamilyName=$old.PackageFamilyName;PackageFullName=$old.PackageFullName;InstallLocation=$junction};$script:packages=@($item);$before=$script:signatureReads;$found=Discover
Check (-not $found.ok -and $script:signatureReads -eq $before) 'registered root reparse rejected before manifest or signature read'
$linked=Package 'linked-executable' '26.2001.9.0';[void](New-Item -ItemType Junction -Path (Join-Path $linked.InstallLocation 'linked') -Target (Join-Path $old.InstallLocation 'app'))
[IO.File]::WriteAllText((Join-Path $linked.InstallLocation 'AppxManifest.xml'),(Manifest '26.2001.9.0' 'linked/ChatGPT.exe'));$script:packages=@($linked);$before=$script:signatureReads;$found=Discover
Check (-not $found.ok -and $script:signatureReads -eq $before) 'manifest executable reparse rejected before signature read'
$core=[IO.File]::ReadAllText((Join-Path $PSScriptRoot '..\src\OfficialIdentity.cs'))
$coreAllowlist=[regex]::Match($core,'internal static bool AllowedSigner\(string signer\)\{return ([^\r\n]+);\}').Groups[1].Value
$signers=@([regex]::Matches($coreAllowlist,'signer=="([^"]+)"')|ForEach-Object {$_.Groups[1].Value})
Check ($signers.Count -eq 3 -and @($signers|Where-Object {$_ -cnotin @('OpenAI OpCo, LLC','OpenAI, L.L.C.','OpenAI')}).Count -eq 0) 'shortcut publisher contract stays synchronized with core exact allowlist'
$summary=[ordered]@{passed=(@($checks|Where-Object {-not $_.passed}).Count -eq 0);checks=$checks.Count;failures=@($checks|Where-Object {-not $_.passed}|ForEach-Object name);mockRegistrationOnly=$true;mockSignatureOnly=$true;actualShortcutsModified=$false;actualCodexStarted=$false;sourceSha256=(Get-FileHash -LiteralPath (Join-Path $PSScriptRoot '..\packaging\OfficialShortcut.ps1') -Algorithm SHA256).Hash}
$summary|ConvertTo-Json -Depth 4|Set-Content -LiteralPath (Join-Path $output 'results.json') -Encoding UTF8
$summary|ConvertTo-Json -Depth 4
if(-not $summary.passed){exit 1}
