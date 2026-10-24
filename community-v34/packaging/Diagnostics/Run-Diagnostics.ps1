[CmdletBinding()]
param([ValidateSet('Menu','Verify','Report','Detailed')][string]$Mode='Report')
Set-StrictMode -Version Latest
$ErrorActionPreference='Stop'
$toolRoot=[IO.Path]::GetDirectoryName($PSCommandPath)
$releaseRoot=[IO.Path]::GetDirectoryName($toolRoot)
$diagnosticLock=$null
$script:currentLocalCheck=$null
# This support helper writes only under Diagnostics/Reports. It never starts
# or stops an application, changes settings, installs software or uses a network.
function Assert-LocalTestPath([string]$Path) {
    $full=[IO.Path]::GetFullPath($Path)
    if($full -notmatch '^[A-Za-z]:\\' -or $full.Substring(2).Contains(':')){throw 'LOCAL-TEST-PATH'}
    $drive=[IO.DriveInfo]::new([IO.Path]::GetPathRoot($full))
    if($drive.DriveType -ne [IO.DriveType]::Fixed -and $drive.DriveType -ne [IO.DriveType]::Removable){throw 'LOCAL-TEST-DRIVE'}
    $parts=[Collections.Generic.List[string]]::new()
    for($part=$full;$part;$part=[IO.Path]::GetDirectoryName($part)){$parts.Add($part)}
    for($i=$parts.Count-1;$i -ge 0;$i--){
        if([IO.File]::Exists($parts[$i]) -or [IO.Directory]::Exists($parts[$i])){
            if(([IO.File]::GetAttributes($parts[$i]) -band [IO.FileAttributes]::ReparsePoint) -ne 0){throw 'LOCAL-TEST-LINK'}
        }
    }
    return $full
}
function New-TestOutput {
    $folder=Assert-LocalTestPath (Join-Path $toolRoot 'Reports')
    [void][IO.Directory]::CreateDirectory($folder)
    return $folder
}
function Save-LocalCheck($Value) {
    $path=Assert-LocalTestPath (Join-Path (New-TestOutput) 'file-check.json')
    [IO.File]::WriteAllText($path,($Value|ConvertTo-Json -Depth 4),[Text.UTF8Encoding]::new($false))
}
function Get-LocalFileRole([string]$Relative) {
    switch -CaseSensitive ($Relative) {
        'Launch.exe'{return 'launcher'}
        'Start-Codex-Dragon.exe'{return 'startup'}
        'WallpaperHost.exe'{return 'wallpaper-host'}
        'WallpaperControls.exe'{return 'wallpaper-controls'}
        'renderer.js'{return 'renderer'}
        'launcher.config.xml'{return 'launcher-config'}
        'output/codex-startup-2k-144fps-crf16.mp4'{return 'startup-media'}
        'output/versions/startup-living-v6/startup-intro.mp4'{return 'intro-media'}
        'output/versions/startup-living-v6/startup-idle.mp4'{return 'idle-media'}
        'output/versions/startup-living-v6/startup-finale.mp4'{return 'finale-media'}
        'output/versions/ai-refined-v4/dragon-wallpaper-ambient-2k-144fps.mp4'{return 'ambient-media'}
        'output/versions/particle-v33/dragon-wallpaper-particles-1440p48.mp4'{return 'particles-media'}
    }
    if($Relative.StartsWith('Diagnostics/',[StringComparison]::Ordinal) -or $Relative -cin @('DiagnosticReport.Common.ps1','DiagnosticAnalysis.ps1','CompatibilityProbe.ps1','EnvironmentDiagnostics.ps1','Collect-DiagnosticReport.ps1')){return 'diagnostics'}
    if($Relative -cin @('Install.ps1','Uninstall.ps1','Install-and-Bind.cmd','OfficialShortcut.ps1','PackageTools.ps1')){return 'installer'}
    if($Relative.StartsWith('source/',[StringComparison]::Ordinal)){return 'source'}
    if($Relative -cmatch '\.(?:md|txt)$'){return 'documentation'}
    return 'other'
}
function Invoke-LocalVerify {
    $watch=[Diagnostics.Stopwatch]::StartNew()
    $record=[ordered]@{schemaVersion=1;passed=$false;fileCount=0;manifestSha256='';elapsedMs=0;failure='verification-failed';failedFileIndex=-1;failedFileRole='none';failureKind='platform-incompatible'}
    $stage='platform-check'
    try {
        . (Join-Path $releaseRoot 'PackageTools.ps1')
        Assert-SupportedPlatform
        $stage='manifest-check'
        $record.failureKind='manifest-invalid'
        $manifest=Read-PackageManifest $releaseRoot
        $record.manifestSha256=Get-Sha256 (Join-Path $releaseRoot 'release-manifest.json')
        $total=@($manifest.files).Count
        $stage='file-integrity'
        foreach($item in $manifest.files){
            $record.failedFileIndex=$record.fileCount;$record.failedFileRole=Get-LocalFileRole $item.path;$record.failureKind='unsafe-path'
            $path=Join-SafePath $releaseRoot $item.path
            $record.failureKind='read-error'
            try{$attributes=[IO.File]::GetAttributes($path)}catch [IO.FileNotFoundException]{$record.failureKind='missing';throw}catch [IO.DirectoryNotFoundException]{$record.failureKind='missing';throw}
            if(($attributes -band [IO.FileAttributes]::Directory) -ne 0){throw 'LOCAL-TEST-INTEGRITY'}
            $length=([IO.FileInfo]::new($path)).Length
            if($length -ne $item.bytes){$record.failureKind='size-mismatch';throw 'LOCAL-TEST-INTEGRITY'}
            $actual=Get-Sha256 $path
            if($actual -cne $item.sha256){$record.failureKind='hash-mismatch';throw 'LOCAL-TEST-INTEGRITY'}
            $record.fileCount++
            if($record.fileCount%10 -eq 0){Write-Progress -Activity '核对所有程序与素材的 SHA-256（Ctrl+C 可取消）' -Status ($record.fileCount.ToString()+'/'+$total) -PercentComplete (100*$record.fileCount/$total)}
        }
        # Inspect assembly metadata without executing it or launching the client.
        $stage='framework-metadata'
        foreach($name in @('Launch.exe','Start-Codex-Dragon.exe','WallpaperControls.exe','WallpaperHost.exe')){
            $record.failureKind='framework-metadata';$record.failedFileRole=Get-LocalFileRole $name;$record.failedFileIndex=-1
            for($i=0;$i -lt $total;$i++){if($manifest.files[$i].path -ceq $name){$record.failedFileIndex=$i;break}}
            $assembly=[Reflection.Assembly]::ReflectionOnlyLoad([IO.File]::ReadAllBytes((Join-Path $releaseRoot $name)))
            $attr=@($assembly.GetCustomAttributesData()|Where-Object {$_.AttributeType.FullName -ceq 'System.Runtime.Versioning.TargetFrameworkAttribute'})
            if($attr.Count -ne 1 -or $attr[0].ConstructorArguments[0].Value -cne '.NETFramework,Version=v4.8'){throw 'LOCAL-TEST-FRAMEWORK'}
        }
        $record.passed=$true;$record.failure='none';$record.failureKind='none';$record.failedFileIndex=-1;$record.failedFileRole='none'
        Write-Host ('PASS：'+$record.fileCount+' 个正式文件及 4 个程序的框架标记核验通过。')
        Write-Host '这确认文件完整，不代表实际播放已通过。请手动双击外层 Launch.exe 测试。'
        try {
            . (Join-Path $releaseRoot 'CompatibilityProbe.ps1')
            $inventory=Get-CodexCompatibilityInventory
            if($inventory.registeredPackageCount -eq 0){Write-Host 'WARN：没有发现当前用户注册的官方 Store Codex。'}
            elseif($inventory.registrationState -ne 'ok'){Write-Host 'WARN：客户端注册信息暂不可用，请收集报告。'}
            else{Write-Host ('官方注册包数：'+$inventory.registeredPackageCount+'。此项不代表壁纸连接已验证。')}
        }catch{Write-Host 'WARN：兼容信息未完成，请收集报告。'}
    }catch{
        $record.failure=$stage
        Write-Host 'FAIL：依赖或文件校验未通过。请收集报告；没有启动或修改应用。'
        Write-Host ('未通过的步骤：'+$stage)
    }finally{
        Write-Progress -Activity '核对所有程序与素材的 SHA-256（Ctrl+C 可取消）' -Completed
        $record.elapsedMs=[Math]::Min(2147483647L,$watch.ElapsedMilliseconds)
        $script:currentLocalCheck=[pscustomobject]$record
        Save-LocalCheck $record
    }
    return [bool]$record.passed
}
function Convert-LocalCheckEvidence($Value,$Report,[switch]$CurrentAttempt) {
    $check=[ordered]@{schemaVersion=1;state='not-run';passed=$false;fileCount=0;elapsedMs=0;failure='unknown';failedFileIndex=-1;failedFileRole='none';failureKind='unknown'}
    if($null -eq $Value){return [pscustomobject]$check}
    try {
        if($Value.schemaVersion -ne 1 -or $Value.passed -isnot [bool] -or $Value.fileCount -isnot [int] -or $Value.fileCount -lt 0 -or $Value.fileCount -gt 300 -or ($Value.elapsedMs -isnot [int] -and $Value.elapsedMs -isnot [long]) -or $Value.elapsedMs -lt 0 -or $Value.elapsedMs -gt 2147483647){return [pscustomobject]$check}
        if(-not $CurrentAttempt -and ($Value.manifestSha256 -isnot [string] -or $Value.manifestSha256 -cnotmatch '^[A-F0-9]{64}$' -or $Value.manifestSha256 -cne $Report.targetManifestSha256)){return [pscustomobject]$check}
        $check.state=if($CurrentAttempt){'current-attempt'}else{'same-manifest'};$check.passed=$Value.passed;$check.fileCount=$Value.fileCount;$check.elapsedMs=$Value.elapsedMs
        if($Value.failure -is [string] -and $Value.failure -cin @('none','platform-check','manifest-check','file-integrity','framework-metadata')){$check.failure=$Value.failure}
        $field=$Value.PSObject.Properties['failedFileIndex'];if($field -and $field.Value -is [int] -and $field.Value -ge -1 -and $field.Value -lt 300){$check.failedFileIndex=$field.Value}
        $field=$Value.PSObject.Properties['failedFileRole'];if($field -and $field.Value -is [string] -and $field.Value -cin @('none','launcher','startup','wallpaper-host','wallpaper-controls','renderer','launcher-config','startup-media','intro-media','idle-media','finale-media','ambient-media','particles-media','diagnostics','installer','documentation','source','other')){$check.failedFileRole=$field.Value}
        $field=$Value.PSObject.Properties['failureKind'];if($field -and $field.Value -is [string] -and $field.Value -cin @('none','missing','size-mismatch','hash-mismatch','unsafe-path','read-error','manifest-invalid','platform-incompatible','framework-metadata','unknown')){$check.failureKind=$field.Value}
    }catch{}
    return [pscustomobject]$check
}
function Invoke-LocalReport($CurrentCheck=$null) {
    . (Join-Path $releaseRoot 'DiagnosticReport.Common.ps1')
    Assert-FixPowerShell
    try {
        $report=Get-PortableDiagnosticReport $releaseRoot
        $report|Add-Member -NotePropertyName manifestState -NotePropertyValue 'valid'
    }catch{
        # A broken extraction must still be reportable. This fallback reads
        # only the same fixed local filenames; it never trusts manifest paths.
        [void](Assert-FixPath $releaseRoot -Directory)
        $hostRuns=@{};$logs=@();foreach($name in @('diagnostics/portable-launch.log','output/wallpaper-readiness.log.1','output/wallpaper-readiness.log','output/handoff-latest.log','output/launcher-error.txt','output/launcher-media-error.txt')){$logs+=Read-FixLogTail $releaseRoot $name $hostRuns}
        $compatibility=[pscustomobject]@{schemaVersion=1;kind='CodexCompatibilityEvidence';registrationState='unavailable';transportSupportVerified=$false}
        try{. (Join-Path $releaseRoot 'CompatibilityProbe.ps1');$compatibility=Get-CodexCompatibilityInventory}catch{}
        $report=[pscustomobject][ordered]@{schemaVersion=1;kind='CodexDragonStartupFixReport';collectedUtc=[DateTime]::UtcNow.ToString('o');manifestState='invalid-or-missing';targetManifestSha256='';declaredFileCount=-1;verifiedFileCountAtPreparation=-1;logByteLimit=262144;logRecordLimit=512;rawLogsIncluded=$false;logs=$logs;compatibility=$compatibility;environment=(Get-PortableEnvironmentEvidence);mediaPresence=(Get-PortableMediaPresence $releaseRoot @());officialPolicy=(Get-PortableIdentityPolicy $releaseRoot)}
    }
    $check=Convert-LocalCheckEvidence $CurrentCheck $report -CurrentAttempt
    $prior=Assert-LocalTestPath (Join-Path $toolRoot 'Reports\file-check.json')
    if($null -eq $CurrentCheck -and [IO.File]::Exists($prior) -and ([IO.FileInfo]::new($prior)).Length -le 4096){
        try {
            $value=[IO.File]::ReadAllText($prior)|ConvertFrom-Json
            $check=Convert-LocalCheckEvidence $value $report
        }catch{}
    }
    . (Join-Path $releaseRoot 'DiagnosticAnalysis.ps1')
    $analysis=Get-CodexDiagnosticAnalysis $report $check
    if($analysis -isnot [string] -or $analysis.Length -gt 16384){throw 'LOCAL-TEST-ANALYSIS'}
    Add-Type -AssemblyName System.IO.Compression
    $folder=New-TestOutput
    $path=Assert-LocalTestPath (Join-Path $folder 'latest-report.zip')
    $summary=Assert-LocalTestPath (Join-Path $folder 'latest-analysis.txt')
    $temp=Assert-LocalTestPath (Join-Path $folder ('report-'+[Guid]::NewGuid().ToString('N')+'.tmp'))
    $summaryTemp=Assert-LocalTestPath (Join-Path $folder ('analysis-'+[Guid]::NewGuid().ToString('N')+'.tmp'))
    $file=[IO.File]::Open($temp,[IO.FileMode]::CreateNew,[IO.FileAccess]::Write,[IO.FileShare]::None)
    $zipComplete=$false
    try {
        $zip=[IO.Compression.ZipArchive]::new($file,[IO.Compression.ZipArchiveMode]::Create,$true)
        try {
            foreach($entry in @(@{name='numeric-report.json';value=$report},@{name='local-file-check.json';value=$check})){
                $stream=$zip.CreateEntry($entry.name).Open()
                try {$bytes=[Text.UTF8Encoding]::new($false).GetBytes(($entry.value|ConvertTo-Json -Depth 10));if($bytes.Length -gt 8388608){throw 'LOCAL-TEST-REPORT-SIZE'};$stream.Write($bytes,0,$bytes.Length)}finally{$stream.Dispose()}
            }
            $stream=$zip.CreateEntry('analysis.txt').Open()
            try{$bytes=[Text.UTF8Encoding]::new($true).GetBytes($analysis);$stream.Write($bytes,0,$bytes.Length)}finally{$stream.Dispose()}
        }finally{$zip.Dispose()}
        $zipComplete=$true
    }finally{$file.Dispose();if(-not $zipComplete -and [IO.File]::Exists($temp)){[void](Assert-LocalTestPath $temp);[IO.File]::Delete($temp)}}
    try {
        [IO.File]::WriteAllText($summaryTemp,$analysis,[Text.UTF8Encoding]::new($true))
        # Fixed latest outputs avoid accumulating report archives. Replace only
        # these two named, checked support outputs; never delete other files.
        # Windows PowerShell 5.1 binds $null to an empty string for a string
        # argument. NullString keeps the optional backup path genuinely null.
        if([IO.File]::Exists($path)){[IO.File]::Replace($temp,$path,[NullString]::Value)}else{[IO.File]::Move($temp,$path)}
        if([IO.File]::Exists($summary)){[IO.File]::Replace($summaryTemp,$summary,[NullString]::Value)}else{[IO.File]::Move($summaryTemp,$summary)}
    }finally{
        foreach($left in @($temp,$summaryTemp)){if([IO.File]::Exists($left)){[void](Assert-LocalTestPath $left);[IO.File]::Delete($left)}}
    }
    Write-Host ('报告 ZIP：'+$path)
    Write-Host ('中文分析：'+$summary)
    Write-Host '只发送这个 ZIP 即可。未导出原始日志、聊天、账号、API Key 或本机路径。'
}
try {
    [void](Assert-LocalTestPath $PSCommandPath)
    [void](Assert-LocalTestPath $releaseRoot)
    foreach($name in @('PackageTools.ps1','DiagnosticReport.Common.ps1','CompatibilityProbe.ps1','EnvironmentDiagnostics.ps1','DiagnosticAnalysis.ps1')){[void](Assert-LocalTestPath (Join-Path $releaseRoot $name))}
    if($PSVersionTable.PSVersion.Major -ne 5 -or $PSVersionTable.PSVersion.Minor -lt 1){throw 'LOCAL-TEST-USE-POWERSHELL-51'}
    # Do not queue work or race the two fixed report outputs when double-clicked
    # repeatedly. The OS releases this per-package lock even after termination.
    $lockPath=Assert-LocalTestPath (Join-Path (New-TestOutput) 'diagnostics.lock')
    try {
        $diagnosticLock=[IO.FileStream]::new($lockPath,[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None,4096,[IO.FileOptions]::DeleteOnClose)
    }catch [IO.IOException]{
        if(($_.Exception.HResult -band 65535) -in @(32,33)){
            Write-Host '已有一份诊断或文件校验正在运行；本次直接退出，没有排队或改写报告。'
            exit 2
        }
        throw
    }
    if($Mode -eq 'Verify'){if(-not (Invoke-LocalVerify)){exit 1};exit 0}
    if($Mode -eq 'Report'){Invoke-LocalReport;exit 0}
    if($Mode -eq 'Detailed'){
        Write-Host '完整诊断：先核对程序与素材，再收集有界日志和环境信息。校验失败也会继续生成报告。'
        [void](Invoke-LocalVerify)
        Invoke-LocalReport -CurrentCheck $script:currentLocalCheck
        exit 0
    }
    while($true){
        Write-Host ''
        Write-Host 'Codex 本地测试：不自动启动、不关闭应用、不等待播放。'
        Write-Host '1  核对依赖、所有程序与素材（不启动应用）'
        Write-Host '2  收集现有日志及兼容信息（随时可收集）'
        Write-Host '3  完整校验并生成报告（推荐反馈问题时使用）'
        Write-Host '0  退出'
        $choice=Read-Host '选择'
        if($choice -eq '0'){break}
        if($choice -eq '1'){[void](Invoke-LocalVerify)}
        elseif($choice -eq '2'){Invoke-LocalReport}
        elseif($choice -eq '3'){[void](Invoke-LocalVerify);Invoke-LocalReport -CurrentCheck $script:currentLocalCheck}
    }
}catch{
    Write-Host '测试工具未完成。没有启动或关闭应用；请保留此窗口说明。'
    Write-Host '已有报告可能是上一次的结果，请不要将它当作本次报告。'
    Write-Host '请确认使用 Windows PowerShell 5.1、完整解压且目录不是链接或网络盘。'
    exit 1
}finally{
    if($null -ne $diagnosticLock){$diagnosticLock.Dispose()}
}
