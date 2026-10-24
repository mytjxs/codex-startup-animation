# Optional, current-user desktop binding. Loading this file has no side effects.
function Assert-ShortcutLocalPath([string]$Path) {
    $full=[IO.Path]::GetFullPath($Path)
    if($full -notmatch '^[A-Za-z]:\\' -or $full.Substring(2).Contains(':') -or $full.Length -gt 2048){throw 'Unsupported local shortcut path.'}
    $parts=[Collections.Generic.List[string]]::new()
    for($part=$full;$part;$part=[IO.Path]::GetDirectoryName($part)){$parts.Add($part)}
    for($i=$parts.Count-1;$i -ge 0;$i--){
        try{$attributes=[IO.File]::GetAttributes($parts[$i])}
        catch [IO.FileNotFoundException]{continue}
        catch [IO.DirectoryNotFoundException]{continue}
        if(($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'Reparse-point shortcut paths are unsupported.'}
    }
    return $full
}
function Get-ShortcutHash([string]$Path) { (Get-FileHash -LiteralPath (Assert-ShortcutLocalPath $Path) -Algorithm SHA256).Hash }
function Initialize-OfficialShortcutNative {
    if('CodexDragonShortcutV1' -as [type]){return}
    Add-Type -ReferencedAssemblies System.Drawing -TypeDefinition @'
using System;
using System.IO;
using System.Text;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.ComTypes;
[ComImport,Guid("00021401-0000-0000-C000-000000000046")] class DragonShellLink {}
[ComImport,Guid("000214F9-0000-0000-C000-000000000046"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface DragonIShellLink {
 void GetPath([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder value,int max,IntPtr find,uint flags);
 void GetIDList(out IntPtr value);void SetIDList(IntPtr value);
 void GetDescription([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder value,int max);void SetDescription([MarshalAs(UnmanagedType.LPWStr)]string value);
 void GetWorkingDirectory([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder value,int max);void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)]string value);
 void GetArguments([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder value,int max);void SetArguments([MarshalAs(UnmanagedType.LPWStr)]string value);
 void GetHotkey(out short value);void SetHotkey(short value);void GetShowCmd(out int value);void SetShowCmd(int value);
 void GetIconLocation([Out,MarshalAs(UnmanagedType.LPWStr)]StringBuilder value,int max,out int index);void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)]string value,int index);
 void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)]string value,uint reserved);void Resolve(IntPtr hwnd,uint flags);void SetPath([MarshalAs(UnmanagedType.LPWStr)]string value);
}
[StructLayout(LayoutKind.Sequential)] struct DragonPropertyKey {public Guid Id;public uint Pid;public DragonPropertyKey(string id,uint pid){Id=new Guid(id);Pid=pid;}}
[StructLayout(LayoutKind.Explicit,Size=24)] struct DragonPropertyValue { [FieldOffset(0)]public ushort Type;[FieldOffset(8)]public IntPtr Text; }
[ComImport,Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface DragonPropertyStore {
 void GetCount(out uint count);void GetAt(uint index,out DragonPropertyKey key);void GetValue(ref DragonPropertyKey key,out DragonPropertyValue value);void SetValue(ref DragonPropertyKey key,ref DragonPropertyValue value);void Commit();
}
[ComImport,Guid("45E2B4AE-B1C3-11D0-B92F-00A0C90312E1"),InterfaceType(ComInterfaceType.InterfaceIsIUnknown)] interface DragonLinkData {
 void AddDataBlock(IntPtr block);void CopyDataBlock(uint signature,out IntPtr block);void RemoveDataBlock(uint signature);void GetFlags(out uint flags);void SetFlags(uint flags);
}
public sealed class CodexDragonShortcutInfo {public string Target,Arguments,WorkingDirectory,Description,Icon,AppId,NamespaceTarget="";public int IconIndex,Show;public short Hotkey;public uint Flags;}
public static class CodexDragonShortcutV1 {
 [DllImport("ole32.dll")] static extern int PropVariantClear(ref DragonPropertyValue value);
 [DllImport("shell32.dll")] static extern int SHGetNameFromIDList(IntPtr pidl,uint kind,out IntPtr text);
 [DllImport("user32.dll",CharSet=CharSet.Unicode)] static extern uint PrivateExtractIcons(string file,int index,int width,int height,IntPtr[] icons,uint[] ids,uint count,uint flags);
 [DllImport("user32.dll")] static extern bool DestroyIcon(IntPtr icon);
 static DragonPropertyKey AppIdKey=new DragonPropertyKey("9F4C2855-9F79-4B39-A8D0-E1D42DE1D5F3",5);
 static string Text(Action<StringBuilder> read){var b=new StringBuilder(2048);read(b);if(b.Length>=2047)throw new InvalidDataException("Shortcut field too long.");return b.ToString();}
 static object Load(string path){var obj=(object)new DragonShellLink();try{((IPersistFile)obj).Load(path,0);return obj;}catch{Marshal.FinalReleaseComObject(obj);throw;}}
 static CodexDragonShortcutInfo Inspect(object obj){
  var link=(DragonIShellLink)obj;var value=new CodexDragonShortcutInfo();
  value.Target=Text(b=>link.GetPath(b,b.Capacity,IntPtr.Zero,4));value.Arguments=Text(b=>link.GetArguments(b,b.Capacity));
  value.WorkingDirectory=Text(b=>link.GetWorkingDirectory(b,b.Capacity));value.Description=Text(b=>link.GetDescription(b,b.Capacity));
  int index=0;value.Icon=Text(b=>link.GetIconLocation(b,b.Capacity,out index));value.IconIndex=index;
  link.GetHotkey(out value.Hotkey);link.GetShowCmd(out value.Show);((DragonLinkData)obj).GetFlags(out value.Flags);
  DragonPropertyValue prop;((DragonPropertyStore)obj).GetValue(ref AppIdKey,out prop);
  try{value.AppId=prop.Type==31&&prop.Text!=IntPtr.Zero?Marshal.PtrToStringUni(prop.Text):"";}finally{PropVariantClear(ref prop);}
  if(String.IsNullOrEmpty(value.Target)){
   IntPtr pidl=IntPtr.Zero,text=IntPtr.Zero;
   try{link.GetIDList(out pidl);if(pidl!=IntPtr.Zero&&SHGetNameFromIDList(pidl,0x80028000,out text)==0&&text!=IntPtr.Zero){string name=Marshal.PtrToStringUni(text);if(name.Length<=2048)value.NamespaceTarget=name;}}
   finally{if(text!=IntPtr.Zero)Marshal.FreeCoTaskMem(text);if(pidl!=IntPtr.Zero)Marshal.FreeCoTaskMem(pidl);}
  }
  return value;
 }
 public static CodexDragonShortcutInfo Read(string path){var obj=Load(path);try{return Inspect(obj);}finally{Marshal.FinalReleaseComObject(obj);}}
 public static void ExportIcon(string source,int index,string output){
  var handles=new IntPtr[1];var ids=new uint[1];
  if(PrivateExtractIcons(source,index,256,256,handles,ids,1,0)!=1||handles[0]==IntPtr.Zero)throw new InvalidDataException("Original icon unavailable.");
  try{using(var icon=(System.Drawing.Icon)System.Drawing.Icon.FromHandle(handles[0]).Clone())using(var stream=new FileStream(output,FileMode.CreateNew,FileAccess.Write,FileShare.None)){icon.Save(stream);stream.Flush(true);}}finally{DestroyIcon(handles[0]);}
 }
 public static void WritePlain(string path,string target,string icon,CodexDragonShortcutInfo original){
  // A NEW filesystem shortcut deliberately omits PIDL/AppID/Relaunch metadata.
  // Retargeting a packaged-app link in place can leave a second activation route.
  var obj=(object)new DragonShellLink();try{
   var link=(DragonIShellLink)obj;link.SetPath(target);link.SetArguments("");link.SetWorkingDirectory(Path.GetDirectoryName(target));
   link.SetIconLocation(icon,0);link.SetDescription(original.Description);link.SetHotkey(original.Hotkey);link.SetShowCmd(original.Show);
   ((IPersistFile)obj).Save(path,true);
  }finally{Marshal.FinalReleaseComObject(obj);}
 }
 // Used by isolated fixtures only; normal installation never creates an official shortcut.
 public static void Fixture(string path,string target,string appId,bool elevated){
  var obj=(object)new DragonShellLink();try{
   var link=(DragonIShellLink)obj;link.SetPath(target);link.SetDescription("fixture description");link.SetWorkingDirectory(Path.GetDirectoryName(target));link.SetIconLocation(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.System),"shell32.dll"),7);link.SetHotkey(0x0643);link.SetShowCmd(1);
   if(!String.IsNullOrEmpty(appId)){var p=new DragonPropertyValue{Type=31,Text=Marshal.StringToCoTaskMemUni(appId)};try{var store=(DragonPropertyStore)obj;store.SetValue(ref AppIdKey,ref p);store.Commit();}finally{PropVariantClear(ref p);}}
   if(elevated){uint flags;((DragonLinkData)obj).GetFlags(out flags);((DragonLinkData)obj).SetFlags(flags|0x2000);}
   ((IPersistFile)obj).Save(path,true);
  }finally{Marshal.FinalReleaseComObject(obj);}
 }
}
'@ -ErrorAction Stop | Out-Null
}
function Get-RegisteredOfficialShortcutIdentity {
    $appIds=[Collections.Generic.List[string]]::new();$executables=[Collections.Generic.List[string]]::new()
    $seenPackages=[Collections.Generic.HashSet[string]]::new([StringComparer]::Ordinal)
    $seenExecutables=[Collections.Generic.HashSet[string]]::new([StringComparer]::OrdinalIgnoreCase)
    $packages=@(Get-AppxPackage -Name OpenAI.Codex -ErrorAction Stop)
    if($packages.Count -gt 32){throw 'Ambiguous official package registration.'}
    foreach($package in $packages){
        if($package.PackageFamilyName -cne 'OpenAI.Codex_2p2nqsd0c76g0'){continue}
        $fullName=[string]$package.PackageFullName
        if([string]::IsNullOrEmpty($fullName) -or $fullName.Length -gt 240){continue}
        $match=[regex]::Match($fullName,'\AOpenAI\.Codex_([0-9]+\.[0-9]+\.[0-9]+\.[0-9]+)_x64__2p2nqsd0c76g0\z')
        $version=[version]'0.0.0.0'
        if(-not $match.Success -or -not [version]::TryParse($match.Groups[1].Value,[ref]$version) -or
            $version.Major -gt 65535 -or $version.Minor -gt 65535 -or $version.Build -gt 65535 -or $version.Revision -gt 65535){continue}
        if(-not $seenPackages.Add($fullName)){throw 'Ambiguous official package registration.'}
        $root=Assert-ShortcutLocalPath $package.InstallLocation
        $manifest=Assert-ShortcutLocalPath (Join-Path $root 'AppxManifest.xml')
        if((Get-Item -LiteralPath $manifest).Length -gt 2097152){throw 'Official manifest is too large.'}
        $settings=[Xml.XmlReaderSettings]::new();$settings.DtdProcessing=[Xml.DtdProcessing]::Prohibit;$settings.XmlResolver=$null;$settings.MaxCharactersInDocument=2097152
        $reader=[Xml.XmlReader]::Create($manifest,$settings)
        try{$xml=[Xml.XmlDocument]::new();$xml.XmlResolver=$null;$xml.Load($reader)}finally{$reader.Dispose()}
        # Match OfficialIdentityResolver.ReadPackage/ManifestExecutable and its
        # exact publisher allowlist. Neither old executable names nor a version
        # directory is retained across discovery calls.
        $identities=$xml.SelectNodes('/*[local-name()="Package"]/*[local-name()="Identity"]')
        if($identities.Count -ne 1 -or $identities[0].GetAttribute('Name') -cne 'OpenAI.Codex' -or
            $identities[0].GetAttribute('ProcessorArchitecture') -cne 'x64' -or $identities[0].GetAttribute('Version') -cne $version.ToString()){throw 'Official manifest identity does not match registration.'}
        $applications=$xml.SelectNodes('/*[local-name()="Package"]/*[local-name()="Applications"]/*[local-name()="Application"][@Id="App"]')
        if($applications.Count -ne 1){throw 'Official App entry is ambiguous.'}
        $relative=$applications[0].GetAttribute('Executable')
        if([string]::IsNullOrWhiteSpace($relative) -or $relative.Length -gt 512 -or [IO.Path]::IsPathRooted($relative) -or
            $relative.IndexOfAny([char[]]@(':',[char]0,'%','$','"')) -ge 0){throw 'Unsafe official manifest executable.'}
        $relative=$relative.Replace('/','\')
        foreach($component in $relative.Split('\')){
            if($component.Length -eq 0 -or $component -eq '.' -or $component -eq '..' -or $component.EndsWith('.') -or $component.EndsWith(' ') -or
                $component.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0){throw 'Unsafe official manifest executable.'}
        }
        $boundary=$root.TrimEnd('\')+'\';$exe=Assert-ShortcutLocalPath (Join-Path $root $relative)
        if(-not $exe.StartsWith($boundary,[StringComparison]::OrdinalIgnoreCase) -or [IO.Path]::GetExtension($exe) -ine '.exe' -or -not [IO.File]::Exists($exe)){throw 'Official manifest executable is unavailable.'}
        $signature=Get-AuthenticodeSignature -LiteralPath $exe
        if($signature.Status -ne 'Valid' -or $null -eq $signature.SignerCertificate){continue}
        $signer=$signature.SignerCertificate.GetNameInfo([Security.Cryptography.X509Certificates.X509NameType]::SimpleName,$false)
        if($signer -cnotin @('OpenAI OpCo, LLC','OpenAI, L.L.C.','OpenAI')){continue}
        if($seenExecutables.Add($exe)){$executables.Add($exe)}
        if($appIds.Count -eq 0){$appIds.Add('OpenAI.Codex_2p2nqsd0c76g0!App')}
        # AppID alone is not authorized until the referenced GUI executable has
        # passed the same identity, path and publisher checks as a direct link.
    }
    return [pscustomobject]@{AppIds=$appIds.ToArray();Executables=$executables.ToArray()}
}
function Test-OfficialShortcutIdentity($Info,$Identity) {
    if(($Info.Flags -band 0x2000) -ne 0){return $false} # Never inherit daily elevation.
    # AppID is only a grouping property and is NOT proof of the activation target.
    # Accept a true AppsFolder PIDL only when its complete parsing name identifies
    # one registered app, and no separate executable or arguments are present.
    if([string]::IsNullOrEmpty($Info.Target) -and [string]::IsNullOrWhiteSpace($Info.Arguments)){
        foreach($id in $Identity.AppIds){
            if([string]::Equals($Info.NamespaceTarget,('::{4234d49b-0245-4df3-b780-3893943456e1}\'+$id),[StringComparison]::OrdinalIgnoreCase)){return $true}
        }
    }
    # Direct executable links must match the registered, verified signed file.
    if(-not [string]::IsNullOrWhiteSpace($Info.Arguments)){return $false}
    foreach($exe in $Identity.Executables){if([string]::Equals($Info.Target,$exe,[StringComparison]::OrdinalIgnoreCase)){return $true}}
    return $false
}
function Write-ShortcutState([string]$Path,$State) {
    [void](Assert-ShortcutLocalPath $Path)
    $temp=Assert-ShortcutLocalPath ($Path+'.'+[Guid]::NewGuid().ToString('N')+'.tmp')
    [IO.File]::WriteAllText($temp,($State|ConvertTo-Json -Depth 5),[Text.UTF8Encoding]::new($false))
    if([IO.File]::Exists($Path)){[IO.File]::Replace($temp,$Path,[NullString]::Value)}else{[IO.File]::Move($temp,$Path)}
}
function Test-OfficialShortcutRecoveryNeeded([string]$InstallationRoot,$InstallState) {
    $root=(Assert-ShortcutLocalPath $InstallationRoot).TrimEnd('\')
    $store=Assert-ShortcutLocalPath (Join-Path $root '.official-shortcut')
    $property=$InstallState.PSObject.Properties['officialBindingRequested']
    if($null -ne $property -and $property.Value -isnot [bool]){throw 'Invalid shortcut ownership marker. Uninstall stopped.'}
    $required=$null -ne $property -and $property.Value -eq $true
    if($required -and -not [IO.Directory]::Exists($store)){throw 'Expected shortcut recovery directory is missing. Uninstall stopped; launcher retained.'}
    return $required -or (Test-Path -LiteralPath $store)
}
function Assert-ShortcutMetadataPreserved($Before,$After) {
    foreach($field in @('Description','Show','Hotkey')){
        if($Before.$field -cne $After.$field){throw 'Shortcut metadata could not be preserved.'}
    }
    if(-not [string]::IsNullOrEmpty($After.AppId)){throw 'Packaged application activation metadata was not removed.'}
    if(($After.Flags -band 0x2000) -ne 0){throw 'Elevated shortcut is unsupported.'}
}
function Set-OfficialDesktopShortcutBinding([string]$InstallationRoot,[string]$Desktop,$Identity) {
    $root=(Assert-ShortcutLocalPath $InstallationRoot).TrimEnd('\');$desktopRoot=(Assert-ShortcutLocalPath $Desktop).TrimEnd('\')
    if([string]::Equals($desktopRoot,[Environment]::GetFolderPath('CommonDesktopDirectory').TrimEnd('\'),[StringComparison]::OrdinalIgnoreCase)){return 'shared-desktop-unsupported'}
    $launch=Assert-ShortcutLocalPath (Join-Path $root 'Launch.exe')
    if(-not [IO.File]::Exists($launch)){throw 'Installed launcher is missing.'}
    Initialize-OfficialShortcutNative
    $candidates=[Collections.Generic.List[object]]::new();$links=@(Get-ChildItem -LiteralPath $desktopRoot -Filter '*.lnk' -File -Force)
    if($links.Count -gt 256){return 'desktop-too-large'}
    foreach($link in $links){
        try{
            $path=Assert-ShortcutLocalPath $link.FullName
            if($link.Length -gt 65536){continue}
            $info=[CodexDragonShortcutV1]::Read($path)
            if(Test-OfficialShortcutIdentity $info $Identity){$candidates.Add([pscustomobject]@{Path=$path;Info=$info})}
        }catch{return 'unreadable-desktop-shortcut'}
    }
    if($candidates.Count -eq 0){return 'no-supported-official-shortcut'}
    if($candidates.Count -ne 1){return 'ambiguous-official-shortcuts'}
    $chosen=$candidates[0];$store=Assert-ShortcutLocalPath (Join-Path $root '.official-shortcut')
    if(Test-Path -LiteralPath $store){throw 'A shortcut binding record already exists; retain it for recovery.'}
    [IO.Directory]::CreateDirectory($store)|Out-Null
    $original=Join-Path $store 'original.lnk';$bound=Join-Path $store 'bound.lnk';$statePath=Join-Path $store 'state.json'
    $originalHash=Get-ShortcutHash $chosen.Path
    [IO.File]::Copy($chosen.Path,$original,$false)
    if((Get-ShortcutHash $original) -cne $originalHash -or (Get-ShortcutHash $chosen.Path) -cne $originalHash){throw 'Shortcut changed during backup; desktop was not replaced.'}
    $iconSource=$chosen.Info.Icon;$iconIndex=$chosen.Info.IconIndex
    if([string]::IsNullOrEmpty($iconSource)){
        $iconIndex=0
        if(@($Identity.Executables).Count -eq 0){throw 'A verified official icon source is unavailable; original shortcut retained.'}
        $iconSource=$Identity.Executables[0]
        foreach($exe in $Identity.Executables){if([string]::Equals($exe,$chosen.Info.Target,[StringComparison]::OrdinalIgnoreCase)){$iconSource=$exe;break}}
    }
    $iconSource=Assert-ShortcutLocalPath ([Environment]::ExpandEnvironmentVariables($iconSource))
    $icon=Join-Path $store 'official.ico';[CodexDragonShortcutV1]::ExportIcon($iconSource,$iconIndex,$icon)
    [CodexDragonShortcutV1]::WritePlain($bound,$launch,$icon,$chosen.Info)
    $after=[CodexDragonShortcutV1]::Read($bound);Assert-ShortcutMetadataPreserved $chosen.Info $after
    if(-not [string]::Equals($after.Target,$launch,[StringComparison]::OrdinalIgnoreCase) -or $after.Arguments -cne '' -or $after.WorkingDirectory -cne $root -or $after.Icon -cne $icon -or $after.IconIndex -ne 0){throw 'Replacement shortcut verification failed.'}
    $boundHash=Get-ShortcutHash $bound
    $state=[ordered]@{schemaVersion=1;kind='CodexDragonOfficialDesktopBinding';phase='prepared';root=$root;desktop=$desktopRoot;linkName=[IO.Path]::GetFileName($chosen.Path);originalSha256=$originalHash;boundSha256=$boundHash;iconSha256=Get-ShortcutHash $icon}
    Write-ShortcutState $statePath $state
    $staged=Assert-ShortcutLocalPath (Join-Path $desktopRoot ('.codex-binding-'+[Guid]::NewGuid().ToString('N')+'.tmp'))
    $displaced=Assert-ShortcutLocalPath (Join-Path $desktopRoot ('.codex-original-'+[Guid]::NewGuid().ToString('N')+'.tmp'))
    [IO.File]::Copy($bound,$staged,$false)
    if((Get-ShortcutHash $staged) -cne $boundHash -or (Get-ShortcutHash $chosen.Path) -cne $originalHash){throw 'Shortcut changed before replacement; original retained.'}
    [void](Assert-ShortcutLocalPath $chosen.Path)
    [IO.File]::Replace($staged,$chosen.Path,$displaced)
    if((Get-ShortcutHash $displaced) -cne $originalHash){
        # Preserve any concurrent desktop edit. Never overwrite a later edit.
        if((Get-ShortcutHash $chosen.Path) -ceq $boundHash){[IO.File]::Replace($displaced,$chosen.Path,[NullString]::Value)}
        throw 'Concurrent shortcut edit detected; installation retained for safe recovery.'
    }
    if((Get-ShortcutHash $chosen.Path) -cne $boundHash){throw 'Shortcut changed after replacement; recovery files retained.'}
    $state.phase='bound';Write-ShortcutState $statePath $state
    if((Get-ShortcutHash $displaced) -ceq $originalHash){[IO.File]::Delete($displaced)}
    return 'bound'
}
function Restore-OfficialDesktopShortcutBinding([string]$InstallationRoot,[string]$Desktop) {
    $root=(Assert-ShortcutLocalPath $InstallationRoot).TrimEnd('\');$desktopRoot=(Assert-ShortcutLocalPath $Desktop).TrimEnd('\')
    $store=Assert-ShortcutLocalPath (Join-Path $root '.official-shortcut')
    if(-not (Test-Path -LiteralPath $store)){return 'not-bound'}
    $statePath=Assert-ShortcutLocalPath (Join-Path $store 'state.json')
    if(-not [IO.File]::Exists($statePath) -or (Get-Item -LiteralPath $statePath).Length -gt 16384){throw 'Shortcut ownership record missing or invalid. Uninstall stopped; files retained.'}
    $state=Get-Content -LiteralPath $statePath -Raw -Encoding UTF8|ConvertFrom-Json
    if($state.schemaVersion -ne 1 -or $state.kind -cne 'CodexDragonOfficialDesktopBinding' -or $state.phase -notin @('prepared','bound','restored') -or
        -not [string]::Equals($state.root,$root,[StringComparison]::OrdinalIgnoreCase) -or -not [string]::Equals($state.desktop,$desktopRoot,[StringComparison]::OrdinalIgnoreCase) -or
        [IO.Path]::GetFileName($state.linkName) -cne $state.linkName -or $state.linkName -notmatch '(?i)\.lnk$' -or $state.linkName.Contains(':') -or
        $state.originalSha256 -cnotmatch '^[A-F0-9]{64}$' -or $state.boundSha256 -cnotmatch '^[A-F0-9]{64}$'){throw 'Shortcut ownership mismatch. Uninstall stopped; files retained.'}
    $link=Assert-ShortcutLocalPath (Join-Path $desktopRoot $state.linkName);$original=Assert-ShortcutLocalPath (Join-Path $store 'original.lnk');$bound=Assert-ShortcutLocalPath (Join-Path $store 'bound.lnk')
    if(-not [IO.File]::Exists($original) -or (Get-ShortcutHash $original) -cne $state.originalSha256 -or -not [IO.File]::Exists($bound) -or (Get-ShortcutHash $bound) -cne $state.boundSha256){throw 'Shortcut recovery bytes failed verification. Uninstall stopped.'}
    if(-not [IO.File]::Exists($link)){throw 'Bound shortcut is missing or moved. Uninstall stopped; recovery files retained.'}
    $current=Get-ShortcutHash $link
    if($current -ceq $state.originalSha256){$state.phase='restored';Write-ShortcutState $statePath $state;return 'already-original'}
    if($current -cne $state.boundSha256){throw 'Bound shortcut was modified. Uninstall stopped to preserve it and the installed launcher.'}
    Initialize-OfficialShortcutNative
    $info=[CodexDragonShortcutV1]::Read($bound)
    if(-not [string]::Equals($info.Target,(Join-Path $root 'Launch.exe'),[StringComparison]::OrdinalIgnoreCase) -or $info.Arguments -cne '' -or ($info.Flags -band 0x2000) -ne 0){throw 'Bound shortcut target mismatch. Uninstall stopped.'}
    $staged=Assert-ShortcutLocalPath (Join-Path $desktopRoot ('.codex-restore-'+[Guid]::NewGuid().ToString('N')+'.tmp'))
    $displaced=Assert-ShortcutLocalPath (Join-Path $desktopRoot ('.codex-bound-'+[Guid]::NewGuid().ToString('N')+'.tmp'))
    [IO.File]::Copy($original,$staged,$false)
    if((Get-ShortcutHash $staged) -cne $state.originalSha256 -or (Get-ShortcutHash $link) -cne $state.boundSha256){throw 'Shortcut changed before restore. Uninstall stopped.'}
    [IO.File]::Replace($staged,$link,$displaced)
    if((Get-ShortcutHash $displaced) -cne $state.boundSha256){
        if((Get-ShortcutHash $link) -ceq $state.originalSha256){[IO.File]::Replace($displaced,$link,[NullString]::Value)}
        throw 'Concurrent shortcut edit detected. Uninstall stopped.'
    }
    if((Get-ShortcutHash $link) -cne $state.originalSha256){throw 'Restored shortcut changed. Uninstall stopped.'}
    $state.phase='restored';Write-ShortcutState $statePath $state
    if((Get-ShortcutHash $displaced) -ceq $state.boundSha256){[IO.File]::Delete($displaced)}
    return 'restored'
}
