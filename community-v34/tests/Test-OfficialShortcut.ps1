param([Parameter(Mandatory=$true)][string]$OutputDirectory)
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
. (Join-Path $PSScriptRoot '..\packaging\OfficialShortcut.ps1')
$output=Assert-ShortcutLocalPath $OutputDirectory
if(Test-Path -LiteralPath $output){throw 'Choose a fresh isolated fixture directory.'}
[IO.Directory]::CreateDirectory($output)|Out-Null
Initialize-OfficialShortcutNative
$checks=[Collections.Generic.List[string]]::new()
function Check([bool]$Condition,[string]$Name){if(-not $Condition){throw ('FAIL: '+$Name)};$checks.Add($Name)}
function Must-Fail([scriptblock]$Action,[string]$Name){$failed=$false;try{&$Action|Out-Null}catch{$failed=$true};Check $failed $Name}
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
$fixtureSource=Join-Path $output 'Fixture.cs'
$fixtureExe=Join-Path $output 'Fixture.exe'
[IO.File]::WriteAllText($fixtureSource,@'
using System;using System.IO;
internal static class Fixture { [STAThread] static void Main(){string path=System.Reflection.Assembly.GetExecutingAssembly().Location;File.WriteAllText(Path.Combine(Path.GetDirectoryName(path),Path.GetFileNameWithoutExtension(path)+".launched"),"isolated fixture only");} }
'@,[Text.UTF8Encoding]::new($false))
& $compiler /nologo /target:winexe /platform:x64 /warnaserror+ /out:$fixtureExe $fixtureSource
if($LASTEXITCODE -ne 0){throw 'Fixture compilation failed.'}
function New-Case([string]$Name){
    $case=Join-Path $output $Name;$desktop=Join-Path $case 'desktop';$installed=Join-Path $case 'installed';$official=Join-Path $case 'official'
    foreach($dir in @($desktop,$installed,$official)){[IO.Directory]::CreateDirectory($dir)|Out-Null}
    $oldExe=Join-Path $official 'Official.exe';$newExe=Join-Path $installed 'Launch.exe'
    [IO.File]::Copy($fixtureExe,$oldExe,$false);[IO.File]::Copy($fixtureExe,$newExe,$false)
    return [pscustomobject]@{Root=$case;Desktop=$desktop;Installed=$installed;OriginalExe=$oldExe;Link=(Join-Path $desktop 'Codex original name.lnk');Identity=[pscustomobject]@{AppIds=@('CodexFixture_0000000000000!App');Executables=@($oldExe)}}
}
function Create-Link($Case,[string]$AppId='', [bool]$Elevated=$false){[CodexDragonShortcutV1]::Fixture($Case.Link,$Case.OriginalExe,$AppId,$Elevated)}
function Read-State($Case){Get-Content -LiteralPath (Join-Path $Case.Installed '.official-shortcut\state.json') -Raw -Encoding UTF8|ConvertFrom-Json}
function Invoke-FixtureLink($Case){
    $start=[Diagnostics.ProcessStartInfo]::new();$start.FileName=$Case.Link;$start.UseShellExecute=$true;$start.WindowStyle=[Diagnostics.ProcessWindowStyle]::Hidden
    $process=[Diagnostics.Process]::Start($start)
    if($null -ne $process){try{if(-not $process.WaitForExit(5000)){throw 'Owned fixture did not exit within five seconds.'}}finally{$process.Dispose()}}
    $marker=Join-Path $Case.Installed 'Launch.launched';$watch=[Diagnostics.Stopwatch]::StartNew()
    while(-not [IO.File]::Exists($marker) -and $watch.ElapsedMilliseconds -lt 2000){Start-Sleep -Milliseconds 25}
    Check ([IO.File]::Exists($marker)) 'ShellExecute reached the isolated installed launcher'
    Check (-not [IO.File]::Exists((Join-Path (Split-Path $Case.OriginalExe) 'Official.launched'))) 'ShellExecute did not activate original target'
}
$direct=New-Case 'plain-exe';Create-Link $direct
$before=[CodexDragonShortcutV1]::Read($direct.Link);$originalHash=Get-ShortcutHash $direct.Link
Check ((Set-OfficialDesktopShortcutBinding $direct.Installed $direct.Desktop $direct.Identity) -eq 'bound') 'unique signed-path fixture binds'
$state=Read-State $direct;$after=[CodexDragonShortcutV1]::Read($direct.Link)
Check ($state.phase -eq 'bound') 'durable state reaches bound'
Check ($state.originalSha256 -ceq $originalHash) 'original hash stored'
Check ((Get-ShortcutHash (Join-Path $direct.Installed '.official-shortcut\original.lnk')) -ceq $originalHash) 'original bytes preserved'
Check ((Get-ShortcutHash $direct.Link) -ceq $state.boundSha256) 'bound bytes hash matches'
Check ([IO.Path]::GetFileName($direct.Link) -ceq 'Codex original name.lnk') 'shortcut name preserved'
foreach($field in @('Description','Hotkey','Show')){Check ($before.$field -ceq $after.$field) ('visible setting preserved: '+$field)}
Check ($after.AppId -ceq '') 'new plain link has no AppID'
Check (($after.Flags -band 0x2000) -eq 0) 'new plain link does not request elevation'
Check ($after.Target -ceq (Join-Path $direct.Installed 'Launch.exe')) 'new target points at stable installed launcher'
Check ($after.WorkingDirectory -ceq $direct.Installed -and $after.Arguments -ceq '') 'new working directory and arguments cannot redirect activation'
$icon=[Drawing.Icon]::new($after.Icon);try{Check ($icon.Width -gt 0 -and $icon.Height -gt 0) 'extracted original icon is readable'}finally{$icon.Dispose()}
Invoke-FixtureLink $direct
Check ((Restore-OfficialDesktopShortcutBinding $direct.Installed $direct.Desktop) -eq 'restored') 'owned shortcut restored before removal'
Check ((Get-ShortcutHash $direct.Link) -ceq $originalHash) 'restoration is byte-for-byte exact'
Check ((Restore-OfficialDesktopShortcutBinding $direct.Installed $direct.Desktop) -eq 'already-original') 'restore is idempotent'

$app=New-Case 'appid';Create-Link $app 'CodexFixture_0000000000000!App';$appHash=Get-ShortcutHash $app.Link
Check (([CodexDragonShortcutV1]::Read($app.Link)).AppId -ceq 'CodexFixture_0000000000000!App') 'fixture actually contains AppID metadata'
Check ((Set-OfficialDesktopShortcutBinding $app.Installed $app.Desktop $app.Identity) -eq 'bound') 'verified executable fixture with AppID binds'
Check (([CodexDragonShortcutV1]::Read($app.Link)).AppId -ceq '') 'AppID activation metadata omitted in new link'
Invoke-FixtureLink $app
Check ((Restore-OfficialDesktopShortcutBinding $app.Installed $app.Desktop) -eq 'restored') 'AppID original restored'
Check ((Get-ShortcutHash $app.Link) -ceq $appHash) 'AppID original including opaque metadata restored exactly'
$spoof=New-Case 'spoofed-appid';Create-Link $spoof 'CodexFixture_0000000000000!App';$spoof.Identity.Executables=@();$hash=Get-ShortcutHash $spoof.Link
Check ((Set-OfficialDesktopShortcutBinding $spoof.Installed $spoof.Desktop $spoof.Identity) -eq 'no-supported-official-shortcut') 'AppID grouping property alone never authenticates arbitrary executable'
Check ((Get-ShortcutHash $spoof.Link) -ceq $hash) 'spoofed AppID shortcut left unchanged'
$pidl=[CodexDragonShortcutInfo]::new();$pidl.Target='';$pidl.Arguments='';$pidl.NamespaceTarget='::{4234d49b-0245-4df3-b780-3893943456e1}\CodexFixture_0000000000000!App'
Check (Test-OfficialShortcutIdentity $pidl $app.Identity) 'exact registered AppsFolder namespace decision accepted'
$pidl.Arguments='--other-command'
Check (-not (Test-OfficialShortcutIdentity $pidl $app.Identity)) 'AppsFolder namespace with extra arguments rejected'
$pidl.Arguments='';$pidl.NamespaceTarget+='-other'
Check (-not (Test-OfficialShortcutIdentity $pidl $app.Identity)) 'AppsFolder namespace suffix rejected'

$ambiguous=New-Case 'ambiguous';Create-Link $ambiguous;[IO.File]::Copy($ambiguous.Link,(Join-Path $ambiguous.Desktop 'second.lnk'));$hash=Get-ShortcutHash $ambiguous.Link
Check ((Set-OfficialDesktopShortcutBinding $ambiguous.Installed $ambiguous.Desktop $ambiguous.Identity) -eq 'ambiguous-official-shortcuts') 'multiple matches are rejected'
Check ((Get-ShortcutHash $ambiguous.Link) -ceq $hash) 'ambiguous link unchanged'
Check (-not (Test-Path -LiteralPath (Join-Path $ambiguous.Installed '.official-shortcut'))) 'ambiguous match creates no binding ownership'

$other=New-Case 'unrelated';Create-Link $other;$other.Identity.Executables=@();$hash=Get-ShortcutHash $other.Link
Check ((Set-OfficialDesktopShortcutBinding $other.Installed $other.Desktop $other.Identity) -eq 'no-supported-official-shortcut') 'unrelated executable rejected'
Check ((Get-ShortcutHash $other.Link) -ceq $hash) 'unrelated shortcut unchanged'
$elevated=New-Case 'elevated';Create-Link $elevated '' $true
Check ((Set-OfficialDesktopShortcutBinding $elevated.Installed $elevated.Desktop $elevated.Identity) -eq 'no-supported-official-shortcut') 'run-as-admin shortcut never inherited'

$changed=New-Case 'user-edit';Create-Link $changed;Set-OfficialDesktopShortcutBinding $changed.Installed $changed.Desktop $changed.Identity|Out-Null
[CodexDragonShortcutV1]::Fixture($changed.Link,$changed.OriginalExe,'',$false);$hash=Get-ShortcutHash $changed.Link
Must-Fail {Restore-OfficialDesktopShortcutBinding $changed.Installed $changed.Desktop} 'user-edited bound link stops uninstall'
Check ((Get-ShortcutHash $changed.Link) -ceq $hash) 'user-edited link retained'
Check ([IO.File]::Exists((Join-Path $changed.Installed 'Launch.exe'))) 'installed launcher retained after unsafe restore refusal'

$corrupt=New-Case 'corrupt-backup';Create-Link $corrupt;Set-OfficialDesktopShortcutBinding $corrupt.Installed $corrupt.Desktop $corrupt.Identity|Out-Null
[IO.File]::AppendAllText((Join-Path $corrupt.Installed '.official-shortcut\original.lnk'),'changed')
Must-Fail {Restore-OfficialDesktopShortcutBinding $corrupt.Installed $corrupt.Desktop} 'corrupt original backup stops uninstall'
$missing=New-Case 'missing-link';Create-Link $missing;Set-OfficialDesktopShortcutBinding $missing.Installed $missing.Desktop $missing.Identity|Out-Null
[IO.File]::Move($missing.Link,(Join-Path $missing.Desktop 'moved.lnk'))
Must-Fail {Restore-OfficialDesktopShortcutBinding $missing.Installed $missing.Desktop} 'moved shortcut stops uninstall'
$missingStore=New-Case 'missing-recovery-directory';Create-Link $missingStore;Set-OfficialDesktopShortcutBinding $missingStore.Installed $missingStore.Desktop $missingStore.Identity|Out-Null
$installState=[pscustomobject]@{officialBindingRequested=$true}
Check (Test-OfficialShortcutRecoveryNeeded $missingStore.Installed $installState) 'binding ownership marker requires recovery'
[IO.Directory]::Move((Join-Path $missingStore.Installed '.official-shortcut'),(Join-Path $missingStore.Root 'moved-recovery'))
Must-Fail {Test-OfficialShortcutRecoveryNeeded $missingStore.Installed $installState} 'missing complete recovery directory stops uninstall before deletion'
Check ([IO.File]::Exists((Join-Path $missingStore.Installed 'Launch.exe'))) 'missing recovery directory retains launcher'
Check (-not (Test-OfficialShortcutRecoveryNeeded $other.Installed ([pscustomobject]@{}))) 'legacy unbound installation requires no recovery'
Check (-not (Test-OfficialShortcutRecoveryNeeded $other.Installed ([pscustomobject]@{officialBindingRequested=$false}))) 'explicitly skipped binding requires no recovery'
Must-Fail {Test-OfficialShortcutRecoveryNeeded $other.Installed ([pscustomobject]@{officialBindingRequested='false'})} 'malformed independent ownership marker stops uninstall'

$prepared=New-Case 'prepared-crash';Create-Link $prepared;$hash=Get-ShortcutHash $prepared.Link;Set-OfficialDesktopShortcutBinding $prepared.Installed $prepared.Desktop $prepared.Identity|Out-Null
$state=Read-State $prepared;$state.phase='prepared';Write-ShortcutState (Join-Path $prepared.Installed '.official-shortcut\state.json') $state
Check ((Restore-OfficialDesktopShortcutBinding $prepared.Installed $prepared.Desktop) -eq 'restored') 'prepared-state crash after atomic replacement is recoverable'
Check ((Get-ShortcutHash $prepared.Link) -ceq $hash) 'prepared-state recovery preserves original bytes'

$linked=New-Case 'linked-desktop';$junction=Join-Path $linked.Root 'junction'
New-Item -ItemType Junction -Path $junction -Target $linked.Desktop|Out-Null
Must-Fail {Assert-ShortcutLocalPath (Join-Path $junction 'unknown.lnk')} 'ancestor reparse rejected before leaf use'
Must-Fail {Set-OfficialDesktopShortcutBinding $linked.Installed $junction $linked.Identity} 'binding rejects reparse desktop'
Must-Fail {Assert-ShortcutLocalPath '\\server\share\Codex.lnk'} 'UNC path rejected'
Must-Fail {Assert-ShortcutLocalPath ($direct.Link+':stream')} 'alternate data stream rejected'

$install=Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\packaging\Install.ps1') -Raw
$uninstall=Get-Content -LiteralPath (Join-Path $PSScriptRoot '..\packaging\Uninstall.ps1') -Raw
Check ($install.Contains('if($CreateDesktopShortcut -and $BindOfficialDesktopShortcut)')) 'two shortcut modes are mutually exclusive'
Check ($install.IndexOf('Write-ShortcutState $installStatePath') -lt $install.IndexOf('Set-OfficialDesktopShortcutBinding')) 'independent ownership record persisted before binding call'
Check ($uninstall.IndexOf('Restore-OfficialDesktopShortcutBinding') -lt $uninstall.IndexOf('Remove-Item')) 'uninstall restores owned link before first deletion'
Check (-not ($install -match '(?i)-Verb\s+RunAs|runas\.exe')) 'installer has no automatic elevation'
$result=[ordered]@{schemaVersion=1;tests=$checks.Count;passed=$true;checks=$checks.ToArray();limitations=@('Synthetic AppID is not a real packaged-app activation test; AppsFolder matching is a separate exact-PIDL-name decision fixture.','No real desktop, official application, registry or installation was modified.','Only owned short-lived fixture WinExe processes were activated.','Original icon was extracted without changing video assets.')}
[IO.File]::WriteAllText((Join-Path $output 'results.json'),($result|ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))
Write-Output ('PASS: '+$checks.Count+' isolated shortcut checks; no real desktop changed.')
