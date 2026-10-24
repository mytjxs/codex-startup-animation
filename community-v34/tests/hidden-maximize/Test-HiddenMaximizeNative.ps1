param(
    [Parameter(Mandatory=$true)][string]$BaselineAssembly,
    [Parameter(Mandatory=$true)][string]$CandidateAssembly,
    [Parameter(Mandatory=$true)][string]$OutputDirectory
)
$ErrorActionPreference='Stop'
$fixtureSource=Join-Path $PSScriptRoot 'HiddenMaximizeNativeFixture.cs'
$output=[IO.Path]::GetFullPath($OutputDirectory)
if(Test-Path -LiteralPath $output){throw 'Choose a fresh fixture output directory.'}
[void][IO.Directory]::CreateDirectory($output)
$fixtureExecutable=Join-Path $output 'HiddenMaximizeNativeFixture.exe'
$compiler=Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
foreach($inputPath in @($BaselineAssembly,$CandidateAssembly,$fixtureSource,$compiler)) {
    if(-not (Test-Path -LiteralPath $inputPath -PathType Leaf)){throw 'A fixture input does not exist.'}
    if($inputPath.Contains('"')){throw 'Unexpected quote in a fixture input path.'}
}
& $compiler /nologo /target:winexe /platform:x64 /optimize+ /r:System.Web.Extensions.dll "/out:$fixtureExecutable" $fixtureSource
if($LASTEXITCODE -ne 0){throw 'Native fixture compilation failed.'}
$results=@()
foreach($fixtureMode in @('red','green')) {
    $assemblyPath=if($fixtureMode -eq 'red'){[IO.Path]::GetFullPath($BaselineAssembly)}else{[IO.Path]::GetFullPath($CandidateAssembly)}
    $outputPath=Join-Path $output ('native-'+$fixtureMode+'.json')
    $fixtureStart=New-Object System.Diagnostics.ProcessStartInfo
    $fixtureStart.FileName=$fixtureExecutable
    $fixtureStart.Arguments='"'+$assemblyPath+'" '+$fixtureMode+' "'+$outputPath+'"'
    $fixtureStart.UseShellExecute=$false
    $fixtureStart.CreateNoWindow=$true
    $fixtureStart.WindowStyle=[System.Diagnostics.ProcessWindowStyle]::Hidden
    $fixtureProcess=[System.Diagnostics.Process]::Start($fixtureStart)
    try {
        if(-not $fixtureProcess.WaitForExit(30000)) {
            # Only this exact process object was created by this test. No app
            # process lookup or broad process-name termination is performed.
            if(-not $fixtureProcess.HasExited){$fixtureProcess.Kill();[void]$fixtureProcess.WaitForExit(2000)}
            throw 'Owned native fixture exceeded its 30-second bound.'
        }
        $result=Get-Content -Raw -LiteralPath $outputPath | ConvertFrom-Json
        if($fixtureProcess.ExitCode -ne 0 -or -not $result.passed){throw ('Native '+$fixtureMode+' assertion failed; see '+$outputPath)}
        if($result.assemblySha256 -ne (Get-FileHash -Algorithm SHA256 -LiteralPath $assemblyPath).Hash){throw 'Assembly changed during native test.'}
        $results += [pscustomobject]@{Mode=$fixtureMode;Passed=$result.passed;Checks=$result.checks.Count;AssemblySHA256=$result.assemblySha256;ElapsedMilliseconds=$result.elapsedMilliseconds;ReportSHA256=(Get-FileHash -Algorithm SHA256 -LiteralPath $outputPath).Hash}
    } finally {$fixtureProcess.Dispose()}
}
$results
