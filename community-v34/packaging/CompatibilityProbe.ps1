# Read-only, bounded compatibility evidence. No process or profile inspection.
# Package identity is obtained from Windows registration; marker files do not
# establish transport support and never imply that an application uses Tauri.
Set-StrictMode -Version Latest

function ConvertTo-CodexProbeVersion([object]$Value) {
    if($Value -isnot [string] -or $Value.Length -gt 43 -or $Value -cnotmatch '\A[0-9]{1,10}(?:\.[0-9]{1,10}){1,3}\z'){return $null}
    $parts=@();foreach($part in $Value.Split('.')){$n=0;if(-not [int]::TryParse($part,[ref]$n) -or $n -lt 0){return $null};$parts+= $n}
    return ,$parts
}

function Get-CodexProbeManifest([object]$Text) {
    $result=[ordered]@{state='unknown';applicationRoles=@();truncated=$false}
    if($null -eq $Text){return [pscustomobject]$result}
    if($Text -isnot [string]){$result.state='malformed';return [pscustomobject]$result}
    if($Text.Length -gt 65536){$result.state='too-large';return [pscustomobject]$result}
    $reader=$null;$inputReader=$null
    try {
        $settings=[Xml.XmlReaderSettings]::new();$settings.DtdProcessing=[Xml.DtdProcessing]::Prohibit;$settings.XmlResolver=$null;$settings.MaxCharactersInDocument=65536
        $inputReader=[IO.StringReader]::new($Text);$reader=[Xml.XmlReader]::Create($inputReader,$settings)
        $xml=[Xml.XmlDocument]::new();$xml.XmlResolver=$null;$xml.Load($reader)
        if($xml.DocumentElement.LocalName -cne 'Package'){$result.state='malformed';return [pscustomobject]$result}
        $applications=$xml.SelectNodes('/*[local-name()="Package"]/*[local-name()="Applications"]/*[local-name()="Application"]')
        $result.state='parsed';$result.truncated=$applications.Count -gt 16
        for($i=0;$i -lt [Math]::Min(16,$applications.Count);$i++){
            $exe=$applications[$i].GetAttribute('Executable');$role='other-relative'
            if([string]::IsNullOrEmpty($exe)){$role='missing'}
            elseif($exe.Length -gt 512 -or $exe -match '[\x00-\x1f\x7f:]' -or $exe.StartsWith('\') -or $exe.StartsWith('/') -or $exe -match '(^|[\\/])\.{1,2}([\\/]|$)'){$role='invalid'}
            elseif($exe.Replace('/','\') -ieq 'app\ChatGPT.exe'){$role='chatgpt-main'}
            elseif($exe.Replace('/','\') -ieq 'app\Codex.exe'){$role='codex-entry'}
            $result.applicationRoles+= $role
        }
    } catch {$result.state='malformed';$result.applicationRoles=@();$result.truncated=$false}
    finally {if($reader){$reader.Dispose()};if($inputReader){$inputReader.Dispose()}}
    return [pscustomobject]$result
}

# Pure projection: no fixture identity, marker string or XML payload is exported.
function ConvertTo-CodexCompatibilityRecord([object]$PackageName,[object]$Markers,[object]$ManifestText,[string]$ReadState='ok') {
    if($PackageName -isnot [string] -or $PackageName.Length -gt 160 -or $PackageName -cnotmatch '\AOpenAI\.Codex_([0-9]{1,10}\.[0-9]{1,10}\.[0-9]{1,10}\.[0-9]{1,10})_(x64|x86|arm64)__2p2nqsd0c76g0\z'){return $null}
    $version=ConvertTo-CodexProbeVersion $Matches[1];$arch=$Matches[2];if($null -eq $version){return $null}
    $evidence=[ordered]@{}
    foreach($key in @('chatgptExe','chromeDll','appAsar','owlRuntimeJson','owlElectronJson','webview2LoaderAtRoot','webview2LoaderAtApp','webview2LoaderAtX64')){
        $value=$null;if($Markers -is [Collections.IDictionary] -and $Markers.Contains($key)){$value=$Markers[$key]}
        $evidence[$key]=if($value -is [bool]){if($value){1}else{0}}else{-1}
    }
    $electron=$evidence.chatgptExe -eq 1 -and $evidence.appAsar -eq 1
    $webview=($evidence.webview2LoaderAtRoot -eq 1 -or $evidence.webview2LoaderAtApp -eq 1 -or $evidence.webview2LoaderAtX64 -eq 1)
    $classification=if($electron -and $webview){'mixed'}elseif($electron){'electron-evidence'}elseif($webview){'webview2-evidence'}else{'unknown'}
    if($ReadState -cnotin @('ok','blocked-root','unavailable','budget-exhausted')){$ReadState='unavailable'}
    return [pscustomobject][ordered]@{version=$version;architecture=$arch;readState=$ReadState;runtimeEvidence=$classification;markers=[pscustomobject]$evidence;manifest=(Get-CodexProbeManifest $ManifestText)}
}

function Test-CodexProbeLocalPath([string]$Path) {
    try {
        if($Path.Length -gt 4096 -or $Path -cnotmatch '\A[A-Za-z]:\\' -or $Path.Substring(2).Contains(':')){return $false}
        $full=[IO.Path]::GetFullPath($Path);if($full.TrimEnd('\') -ine $Path.TrimEnd('\')){return $false}
        $drive=[IO.DriveInfo]::new([IO.Path]::GetPathRoot($full))
        if($drive.DriveType -notin @([IO.DriveType]::Fixed,[IO.DriveType]::Removable)){return $false}
        $chain=[Collections.Generic.List[string]]::new()
        for($cursor=$full;$cursor;$cursor=[IO.Path]::GetDirectoryName($cursor)){
            if($chain.Count -ge 64){return $false};$chain.Add($cursor)
        }
        # Check ancestors before descendants: probing the child first could
        # already follow a junction before discovering its reparse ancestor.
        for($i=$chain.Count-1;$i -ge 0;$i--){
            try{$attributes=[IO.File]::GetAttributes($chain[$i])}catch [IO.FileNotFoundException]{return $true}catch [IO.DirectoryNotFoundException]{return $true}
            if(($attributes -band [IO.FileAttributes]::ReparsePoint) -ne 0){return $false}
        }
        return $true
    } catch {return $false}
}

function Read-CodexProbePackage([string]$PackageName,[string]$Root,[Diagnostics.Stopwatch]$Clock,[int]$BudgetMilliseconds) {
    if(-not (Test-CodexProbeLocalPath $Root)){return ConvertTo-CodexCompatibilityRecord $PackageName @{} $null 'blocked-root'}
    $markers=[ordered]@{};$manifest=$null;$state='ok'
    $paths=[ordered]@{chatgptExe='app\ChatGPT.exe';chromeDll='app\chrome.dll';appAsar='app\resources\app.asar';owlRuntimeJson='app\owl-shell-runtime.json';owlElectronJson='app\resources\owl-electron-app.json';webview2LoaderAtRoot='WebView2Loader.dll';webview2LoaderAtApp='app\WebView2Loader.dll';webview2LoaderAtX64='app\runtimes\win-x64\native\WebView2Loader.dll'}
    foreach($key in $paths.Keys){
        if($Clock.ElapsedMilliseconds -ge $BudgetMilliseconds){$state='budget-exhausted';break}
        $path=[IO.Path]::Combine($Root,$paths[$key])
        if(-not (Test-CodexProbeLocalPath $path)){$state='unavailable';continue}
        try{$attr=[IO.File]::GetAttributes($path);$markers[$key]=($attr -band [IO.FileAttributes]::Directory) -eq 0}
        catch [IO.FileNotFoundException]{$markers[$key]=$false}
        catch [IO.DirectoryNotFoundException]{$markers[$key]=$false}
        catch {$state='unavailable'}
    }
    $manifestPath=[IO.Path]::Combine($Root,'AppxManifest.xml')
    if($Clock.ElapsedMilliseconds -lt $BudgetMilliseconds -and (Test-CodexProbeLocalPath $manifestPath)){
        $stream=$null;$reader=$null
        try{
            $stream=[IO.FileStream]::new($manifestPath,[IO.FileMode]::Open,[IO.FileAccess]::Read,[IO.FileShare]::Read,4096,[IO.FileOptions]::SequentialScan)
            if($stream.Length -le 65536){
                $reader=[IO.StreamReader]::new($stream,[Text.Encoding]::UTF8,$true,4096)
                # A bounded read remains bounded even if an unusual filesystem
                # changes the file after the byte-length observation.
                $buffer=New-Object char[] 65537;$read=$reader.ReadBlock($buffer,0,$buffer.Length);$manifest=[string]::new($buffer,0,$read)
            }else{$manifest='x'*65537}
        }catch{$state='unavailable'}finally{if($reader){$reader.Dispose()}elseif($stream){$stream.Dispose()}}
    }elseif($Clock.ElapsedMilliseconds -ge $BudgetMilliseconds){$state='budget-exhausted'}
    return ConvertTo-CodexCompatibilityRecord $PackageName $markers $manifest $state
}

function Initialize-CodexProbeNative {
    if('CodexDragonCompatibilityV1Native' -as [type]){return}
    Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
using System.Text;
public sealed class CodexDragonCompatibilityV1Enumeration {
    public string Status="unavailable"; public int Count=-1; public bool Capped; public string[] Names=new string[0];
}
public static class CodexDragonCompatibilityV1Native {
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern int GetPackagesByPackageFamily(string family,ref uint count,IntPtr names,ref uint length,IntPtr buffer);
    [DllImport("kernel32.dll",CharSet=CharSet.Unicode)] static extern int GetPackagePathByFullName(string name,ref uint length,StringBuilder path);
    public static CodexDragonCompatibilityV1Enumeration Enumerate() {
        var result=new CodexDragonCompatibilityV1Enumeration(); uint count=0,length=0;
        int code=GetPackagesByPackageFamily("OpenAI.Codex_2p2nqsd0c76g0",ref count,IntPtr.Zero,ref length,IntPtr.Zero);
        if(code==0&&count==0){result.Status="ok";result.Count=0;return result;}
        if(code!=122)return result;
        result.Count=(int)Math.Min(32U,count);
        if(count>32||length>32768){result.Status="capped";result.Capped=true;return result;}
        if(count==0||length<2)return result;
        IntPtr names=IntPtr.Zero,buffer=IntPtr.Zero;
        try {
            uint capacity=count,characters=length;
            names=Marshal.AllocHGlobal(checked((int)count*IntPtr.Size));buffer=Marshal.AllocHGlobal(checked((int)length*2));
            code=GetPackagesByPackageFamily("OpenAI.Codex_2p2nqsd0c76g0",ref count,names,ref length,buffer);
            if(code!=0||count>capacity||length>characters)return result;
            var values=new string[count];long first=buffer.ToInt64(),last=first+characters*2;
            for(int i=0;i<count;i++) {
                IntPtr ptr=Marshal.ReadIntPtr(names,i*IntPtr.Size);long address=ptr.ToInt64();
                if(address<first||address>=last||(address-first)%2!=0)return result;
                int available=(int)Math.Min(161L,(last-address)/2);string item=Marshal.PtrToStringUni(ptr,available);int end=item.IndexOf('\0');
                if(end<0||end>160)return result;values[i]=item.Substring(0,end);
            }
            result.Status="ok";result.Count=(int)count;result.Names=values;return result;
        } finally {if(names!=IntPtr.Zero)Marshal.FreeHGlobal(names);if(buffer!=IntPtr.Zero)Marshal.FreeHGlobal(buffer);}
    }
    public static string Root(string name) {
        uint length=0;if(GetPackagePathByFullName(name,ref length,null)!=122||length<2||length>4096)return null;
        var path=new StringBuilder((int)length);return GetPackagePathByFullName(name,ref length,path)==0?path.ToString():null;
    }
}
'@ -ErrorAction Stop | Out-Null
}

function Get-CodexCompatibilityInventory([ValidateRange(200,3000)][int]$BudgetMilliseconds=1500) {
    $clock=[Diagnostics.Stopwatch]::StartNew()
    $result=[ordered]@{schemaVersion=1;kind='CodexCompatibilityEvidence';windowsVersion=$null;dotNetFrameworkRelease=-1;powerShellVersion=$null;registrationState='unavailable';registeredPackageCount=-1;packageCountCapped=$false;packages=@();budgetExhausted=$false;elapsedMs=0;transportSupportVerified=$false}
    try{$result.windowsVersion=ConvertTo-CodexProbeVersion ([Environment]::OSVersion.Version.ToString())}catch{}
    try{$result.powerShellVersion=ConvertTo-CodexProbeVersion ($PSVersionTable.PSVersion.ToString())}catch{}
    $key=$null
    try{$key=[Microsoft.Win32.Registry]::LocalMachine.OpenSubKey('SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full');if($key){$value=$key.GetValue('Release');if($value -is [int] -and $value -ge 0 -and $value -le 9999999){$result.dotNetFrameworkRelease=$value}}}catch{}finally{if($key){$key.Dispose()}}
    try{
        if($clock.ElapsedMilliseconds -lt $BudgetMilliseconds){Initialize-CodexProbeNative}
        if($clock.ElapsedMilliseconds -lt $BudgetMilliseconds){
            $registered=[CodexDragonCompatibilityV1Native]::Enumerate();$result.registrationState=$registered.Status;$result.registeredPackageCount=$registered.Count;$result.packageCountCapped=$registered.Capped
            foreach($name in $registered.Names){
                if($clock.ElapsedMilliseconds -ge $BudgetMilliseconds){break}
                if($null -eq (ConvertTo-CodexCompatibilityRecord $name @{} $null)){continue}
                $path=[CodexDragonCompatibilityV1Native]::Root($name)
                if([string]::IsNullOrEmpty($path)){$record=ConvertTo-CodexCompatibilityRecord $name @{} $null 'unavailable'}else{$record=Read-CodexProbePackage $name $path $clock $BudgetMilliseconds}
                if($null -ne $record){$result.packages+= $record}
            }
        }
    }catch{$result.registrationState='unavailable'}
    $result.elapsedMs=[Math]::Min(2147483647L,$clock.ElapsedMilliseconds);$result.budgetExhausted=$clock.ElapsedMilliseconds -ge $BudgetMilliseconds
    return [pscustomobject]$result
}
