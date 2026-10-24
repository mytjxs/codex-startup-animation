# Read-only system facts. No WMI, child processes, application state, network,
# account/profile inspection, hardware identifiers or free-form output fields.
Set-StrictMode -Version Latest

function Get-CodexEnvironmentNumber([object]$Value,[long]$Maximum=2147483647) {
    if($Value -isnot [byte] -and $Value -isnot [int16] -and $Value -isnot [int] -and $Value -isnot [long] -and $Value -isnot [uint16] -and $Value -isnot [uint32] -and $Value -isnot [uint64]){return -1L}
    if($Value -lt 0 -or $Value -gt $Maximum){return -1L};return [long]$Value
}
function Get-CodexEnvironmentField([object]$Data,[string]$Key) {
    if($Data -is [Collections.IDictionary] -and $Data.Contains($Key)){return ,$Data[$Key]};return $null
}
function ConvertTo-CodexEnvironmentVersion([object]$Value) {
    if($Value -isnot [string] -or $Value.Length -gt 43 -or $Value -cnotmatch '\A[0-9]{1,10}(?:\.[0-9]{1,10}){1,3}\z'){return ,@()}
    $parts=@();foreach($part in $Value.Split('.')){$number=0;if(-not [int]::TryParse($part,[ref]$number) -or $number -lt 0){return ,@()};$parts+=$number};return ,$parts
}
function ConvertTo-CodexEnvironmentDate([object]$Value) {
    if($Value -isnot [string] -or $Value.Length -gt 10){return ,@()}
    $year=0;$month=0;$day=0
    if($Value -cmatch '\A([0-9]{1,2})[-/]([0-9]{1,2})[-/]([0-9]{4})\z'){$month=[int]$Matches[1];$day=[int]$Matches[2];$year=[int]$Matches[3]}
    elseif($Value -cmatch '\A([0-9]{4})-([0-9]{2})-([0-9]{2})\z'){$year=[int]$Matches[1];$month=[int]$Matches[2];$day=[int]$Matches[3]}
    else{return ,@()}
    if($year -lt 1990 -or $year -gt 2100 -or $month -lt 1 -or $month -gt 12 -or $day -lt 1){return ,@()}
    if($day -gt [DateTime]::DaysInMonth($year,$month)){return ,@()};return ,@($year,$month,$day)
}
function ConvertTo-CodexEnvironmentGpu([object]$Data) {
    $provider=Get-CodexEnvironmentField $Data 'provider';$vendor='unknown'
    if($provider -is [string] -and $provider.Length -le 64){
        switch -Regex ($provider) {
            '\A(?:NVIDIA|NVIDIA Corporation)\z' {$vendor='nvidia';break}
            '\A(?:Advanced Micro Devices, Inc\.|Advanced Micro Devices|AMD|ATI Technologies Inc\.)\z' {$vendor='amd';break}
            '\A(?:Intel|Intel Corporation)\z' {$vendor='intel';break}
            '\A(?:Microsoft|Microsoft Corporation)\z' {$vendor='microsoft';break}
        }
    }
    return [pscustomobject][ordered]@{vendor=$vendor;driverVersion=(ConvertTo-CodexEnvironmentVersion (Get-CodexEnvironmentField $Data 'version'));driverDate=(ConvertTo-CodexEnvironmentDate (Get-CodexEnvironmentField $Data 'date'))}
}

# Pure allowlist projection also used by adversarial offline fixtures. Registry
# provider names/dates are input only; no raw string survives this boundary.
function ConvertTo-CodexEnvironmentEvidence([object]$Data) {
    $result=[ordered]@{schemaVersion=1;kind='CodexEnvironmentEvidence';windowsVersion=(ConvertTo-CodexEnvironmentVersion (Get-CodexEnvironmentField $Data 'windowsVersion'));osArchitecture='unknown';processArchitecture='unknown';dotNetFrameworkRelease=-1L;logicalProcessors=-1L;memory=$null;display=$null;gpu=$null;privileges=$null;budgetMs=0L;elapsedMs=0L;budgetExhausted=$false;readFailures=0L}
    foreach($key in @('osArchitecture','processArchitecture')){$value=Get-CodexEnvironmentField $Data $key;if($value -is [string] -and $value -cin @('x86','x64','arm64')){$result[$key]=$value}}
    $result.dotNetFrameworkRelease=Get-CodexEnvironmentNumber (Get-CodexEnvironmentField $Data 'dotNetFrameworkRelease') 9999999
    $result.logicalProcessors=Get-CodexEnvironmentNumber (Get-CodexEnvironmentField $Data 'logicalProcessors') 4096
    $memory=[ordered]@{};foreach($key in @('totalPhysicalBytes','availablePhysicalBytes','totalCommitBytes','availableCommitBytes')){$memory[$key]=Get-CodexEnvironmentNumber (Get-CodexEnvironmentField $Data $key) 281474976710656};$result.memory=[pscustomobject]$memory
    $display=[ordered]@{metricSpace='process-coordinate-space';dpiAwareness='unknown'}
    foreach($key in @('monitorCount','primaryWidth','primaryHeight','virtualWidth','virtualHeight','systemDpi','userLogPixels','machineLogPixels','primaryScalePercent')){$max=if($key -eq 'monitorCount'){128}elseif($key -eq 'primaryScalePercent'){500}elseif($key -match 'Dpi|Pixels'){960}else{131072};$display[$key]=Get-CodexEnvironmentNumber (Get-CodexEnvironmentField $Data $key) $max}
    $aware=Get-CodexEnvironmentField $Data 'dpiAwareness';if($aware -is [string] -and $aware -cin @('unaware','system','per-monitor')){$display.dpiAwareness=$aware};$result.display=[pscustomobject]$display
    $drivers=@();$values=Get-CodexEnvironmentField $Data 'gpuDrivers';if($values -is [array] -or $values -is [Collections.IList]){for($i=0;$i -lt [Math]::Min(8,$values.Count);$i++){$drivers+=ConvertTo-CodexEnvironmentGpu $values[$i]}}
    $state=Get-CodexEnvironmentField $Data 'gpuState';if($state -isnot [string] -or $state -cnotin @('registry-evidence','budget-exhausted')){$state='unknown'}
    $nodes=Get-CodexEnvironmentNumber (Get-CodexEnvironmentField $Data 'gpuScannedNodes') 32
    $cap=Get-CodexEnvironmentField $Data 'gpuCapped';$result.gpu=[pscustomobject][ordered]@{state=$state;evidence='registered-driver-not-active-load';scannedNodes=$nodes;capped=($cap -is [bool] -and $cap);drivers=$drivers}
    $elevated=Get-CodexEnvironmentField $Data 'elevated';$result.privileges=[pscustomobject]@{elevationKnown=($elevated -is [bool]);elevated=($elevated -is [bool] -and $elevated)}
    foreach($key in @('budgetMs','elapsedMs','readFailures')){$max=if($key -eq 'budgetMs'){2000}elseif($key -eq 'readFailures'){128}else{2147483647};$value=Get-CodexEnvironmentNumber (Get-CodexEnvironmentField $Data $key) $max;$result[$key]=[Math]::Max(0L,$value)}
    $exhausted=Get-CodexEnvironmentField $Data 'budgetExhausted';$result.budgetExhausted=($exhausted -is [bool] -and $exhausted)
    return [pscustomobject]$result
}

function Get-CodexEnvironmentNativeType {
    $existing='CodexDragonEnvironmentEvidenceNativeV1' -as [type];if($existing){return $existing}
    # Reflection.Emit creates declarations in memory: unlike Add-Type source
    # compilation on Windows PowerShell, no compiler executable is started.
    $name=[Reflection.AssemblyName]::new('CodexDragonEnvironmentEvidenceDynamicV1')
    $assembly=[AppDomain]::CurrentDomain.DefineDynamicAssembly($name,[Reflection.Emit.AssemblyBuilderAccess]::Run)
    $module=$assembly.DefineDynamicModule($name.Name)
    $builder=$module.DefineType('CodexDragonEnvironmentEvidenceNativeV1',[Reflection.TypeAttributes]::Public -bor [Reflection.TypeAttributes]::Abstract -bor [Reflection.TypeAttributes]::Sealed)
    $flags=[Reflection.MethodAttributes]::Public -bor [Reflection.MethodAttributes]::Static -bor [Reflection.MethodAttributes]::PinvokeImpl
    $specs=@(
        @{name='GlobalMemoryStatusEx';dll='kernel32.dll';result=[bool];args=[type[]]@([IntPtr])},
        @{name='GetSystemMetrics';dll='user32.dll';result=[int];args=[type[]]@([int])},
        @{name='GetDpiForSystem';dll='user32.dll';result=[uint32];args=[type[]]@()},
        @{name='GetThreadDpiAwarenessContext';dll='user32.dll';result=[IntPtr];args=[type[]]@()},
        @{name='GetAwarenessFromDpiAwarenessContext';dll='user32.dll';result=[int];args=[type[]]@([IntPtr])},
        @{name='MonitorFromWindow';dll='user32.dll';result=[IntPtr];args=[type[]]@([IntPtr],[uint32])},
        @{name='GetScaleFactorForMonitor';dll='shcore.dll';result=[int];args=[type[]]@([IntPtr],[int].MakeByRefType())}
    )
    foreach($spec in $specs){$method=$builder.DefinePInvokeMethod($spec.name,$spec.dll,$spec.name,$flags,[Reflection.CallingConventions]::Standard,$spec.result,$spec.args,[Runtime.InteropServices.CallingConvention]::Winapi,[Runtime.InteropServices.CharSet]::Unicode);$method.SetImplementationFlags($method.GetMethodImplementationFlags() -bor [Reflection.MethodImplAttributes]::PreserveSig)}
    return $builder.CreateType()
}

function Get-CodexEnvironmentEvidence([ValidateRange(0,2000)][int]$BudgetMilliseconds=1500) {
    $clock=[Diagnostics.Stopwatch]::StartNew();$raw=[ordered]@{budgetMs=$BudgetMilliseconds;readFailures=0;gpuDrivers=@();gpuScannedNodes=0;gpuState='unknown';gpuCapped=$false}
    # Every native call and fixed local-registry read is preceded by a deadline
    # check. No wait, recursive enumeration, remote registry or disk scan exists.
    if($clock.ElapsedMilliseconds -lt $BudgetMilliseconds){try{$raw.windowsVersion=[Environment]::OSVersion.Version.ToString();$raw.logicalProcessors=[Environment]::ProcessorCount;if('System.Runtime.InteropServices.RuntimeInformation' -as [type]){$raw.processArchitecture=[Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture.ToString().ToLowerInvariant()}elseif(-not [Environment]::Is64BitProcess){$raw.processArchitecture='x86'}}catch{$raw.readFailures++}}
    $identity=$null
    if($clock.ElapsedMilliseconds -lt $BudgetMilliseconds){try{$identity=[Security.Principal.WindowsIdentity]::GetCurrent();$principal=[Security.Principal.WindowsPrincipal]::new($identity);$raw.elevated=$principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)}catch{$raw.readFailures++}finally{if($identity){$identity.Dispose()}}}
    $reads=@(
        @{hive='machine';path='SYSTEM\CurrentControlSet\Control\Session Manager\Environment';value='PROCESSOR_ARCHITECTURE';field='architecture'},
        @{hive='machine';path='SOFTWARE\Microsoft\NET Framework Setup\NDP\v4\Full';value='Release';field='dotNetFrameworkRelease'},
        @{hive='user';path='Control Panel\Desktop';value='LogPixels';field='userLogPixels'},
        @{hive='machine';path='SOFTWARE\Microsoft\Windows NT\CurrentVersion\FontDPI';value='LogPixels';field='machineLogPixels'}
    )
    foreach($read in $reads){if($clock.ElapsedMilliseconds -ge $BudgetMilliseconds){break};$key=$null
        try{$base=if($read.hive -eq 'machine'){[Microsoft.Win32.Registry]::LocalMachine}else{[Microsoft.Win32.Registry]::CurrentUser};$key=$base.OpenSubKey($read.path,$false);if($key){$value=$key.GetValue($read.value,$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames);if($read.field -eq 'architecture'){if($value -is [string]){switch -Exact ($value){'AMD64'{$raw.osArchitecture='x64'}'ARM64'{$raw.osArchitecture='arm64'}'x86'{$raw.osArchitecture='x86'}}}}else{$raw[$read.field]=$value}}}catch{$raw.readFailures++}finally{if($key){$key.Dispose()}}
    }
    $native=$null
    if($clock.ElapsedMilliseconds -lt $BudgetMilliseconds){try{$native=Get-CodexEnvironmentNativeType}catch{$raw.readFailures++}}
    if($native -and $clock.ElapsedMilliseconds -lt $BudgetMilliseconds){$buffer=[IntPtr]::Zero
        try{$buffer=[Runtime.InteropServices.Marshal]::AllocHGlobal(64);for($i=0;$i -lt 64;$i+=4){[Runtime.InteropServices.Marshal]::WriteInt32($buffer,$i,0)};[Runtime.InteropServices.Marshal]::WriteInt32($buffer,64)
            if($native.GetMethod('GlobalMemoryStatusEx').Invoke($null,[object[]]@($buffer))){$raw.totalPhysicalBytes=[Runtime.InteropServices.Marshal]::ReadInt64($buffer,8);$raw.availablePhysicalBytes=[Runtime.InteropServices.Marshal]::ReadInt64($buffer,16);$raw.totalCommitBytes=[Runtime.InteropServices.Marshal]::ReadInt64($buffer,24);$raw.availableCommitBytes=[Runtime.InteropServices.Marshal]::ReadInt64($buffer,32)}
        }catch{$raw.readFailures++}finally{if($buffer -ne [IntPtr]::Zero){[Runtime.InteropServices.Marshal]::FreeHGlobal($buffer)}}
    }
    foreach($metric in @(@{field='monitorCount';index=80},@{field='primaryWidth';index=0},@{field='primaryHeight';index=1},@{field='virtualWidth';index=78},@{field='virtualHeight';index=79})){
        if(-not $native -or $clock.ElapsedMilliseconds -ge $BudgetMilliseconds){break};try{$raw[$metric.field]=$native.GetMethod('GetSystemMetrics').Invoke($null,[object[]]@([int]$metric.index))}catch{$raw.readFailures++}
    }
    if($native -and $clock.ElapsedMilliseconds -lt $BudgetMilliseconds){try{$raw.systemDpi=$native.GetMethod('GetDpiForSystem').Invoke($null,[object[]]@());$context=$native.GetMethod('GetThreadDpiAwarenessContext').Invoke($null,[object[]]@());$aware=$native.GetMethod('GetAwarenessFromDpiAwarenessContext').Invoke($null,[object[]]@($context));switch($aware){0{$raw.dpiAwareness='unaware'}1{$raw.dpiAwareness='system'}2{$raw.dpiAwareness='per-monitor'}}}catch{$raw.readFailures++}}
    if($native -and $clock.ElapsedMilliseconds -lt $BudgetMilliseconds){try{$monitor=$native.GetMethod('MonitorFromWindow').Invoke($null,[object[]]@([IntPtr]::Zero,[uint32]1));if($monitor -ne [IntPtr]::Zero){$arguments=[object[]]@($monitor,[int]0);if($native.GetMethod('GetScaleFactorForMonitor').Invoke($null,$arguments) -eq 0){$raw.primaryScalePercent=$arguments[1]}}}catch{$raw.readFailures++}}
    # These are registered display-driver entries, not proof of currently active
    # GPUs. No adapter description, device ID, serial or monitor name is read.
    for($i=0;$i -lt 32;$i++){
        if($clock.ElapsedMilliseconds -ge $BudgetMilliseconds){$raw.gpuState='budget-exhausted';break};if($raw.gpuDrivers.Count -ge 8){$raw.gpuCapped=$true;break};$key=$null;$raw.gpuScannedNodes++
        try{$key=[Microsoft.Win32.Registry]::LocalMachine.OpenSubKey(('SYSTEM\CurrentControlSet\Control\Class\{4d36e968-e325-11ce-bfc1-08002be10318}\'+$i.ToString('D4')),$false)
            if($key){$raw.gpuState='registry-evidence';$driver=@{};foreach($field in @(@{value='ProviderName';field='provider'},@{value='DriverVersion';field='version'},@{value='DriverDate';field='date'})){if($clock.ElapsedMilliseconds -ge $BudgetMilliseconds){$raw.gpuState='budget-exhausted';break};$driver[$field.field]=$key.GetValue($field.value,$null,[Microsoft.Win32.RegistryValueOptions]::DoNotExpandEnvironmentNames)};$raw.gpuDrivers+=,$driver}
        }catch{$raw.readFailures++}finally{if($key){$key.Dispose()}}
    }
    $raw.budgetExhausted=$clock.ElapsedMilliseconds -ge $BudgetMilliseconds;$raw.elapsedMs=$clock.ElapsedMilliseconds
    $result=ConvertTo-CodexEnvironmentEvidence $raw
    $result.elapsedMs=[Math]::Min(2147483647L,$clock.ElapsedMilliseconds);$result.budgetExhausted=$result.budgetExhausted -or $clock.ElapsedMilliseconds -ge $BudgetMilliseconds
    return $result
}
