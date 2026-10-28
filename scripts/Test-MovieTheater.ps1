[CmdletBinding()]
param(
    [string] $DotNetExecutable = 'dotnet',
    [string] $OutputRoot = ''
)

$ErrorActionPreference = 'Stop'
$runtime = (Get-Command -Name $DotNetExecutable -ErrorAction Stop).Source
$repositoryRoot = (Resolve-Path -LiteralPath (Join-Path $PSScriptRoot '..')).Path
if ([string]::IsNullOrWhiteSpace($OutputRoot)) { $OutputRoot = Join-Path $repositoryRoot 'artifacts/theater-qa' }
$runName = [DateTime]::UtcNow.ToString('yyyyMMdd-HHmmss') + '-' + [Guid]::NewGuid().ToString('N').Substring(0, 8)
$runDirectory = Join-Path ([IO.Path]::GetFullPath($OutputRoot)) $runName
New-Item -ItemType Directory -Path $runDirectory -Force | Out-Null
$checks = [Collections.Generic.List[object]]::new()
$commit = (& git -C $repositoryRoot rev-parse HEAD).Trim()
$dirty = [bool](& git -C $repositoryRoot status --porcelain)
$report = [ordered]@{
    schemaVersion = 1
    startedUtc = [DateTime]::UtcNow.ToString('o')
    completedUtc = $null
    commit = $commit
    workingTreeDirty = $dirty
    environment = 'One Windows PC; deterministic application-level TCP relay impairment, not physical packet loss or two-PC certification.'
    status = 'running'
    checks = $checks
}

function Invoke-TheaterCheck([string] $Name, [string[]] $CommandArguments) {
    $clock = [Diagnostics.Stopwatch]::StartNew()
    $entry = [ordered]@{ name = $Name; status = 'running'; exitCode = $null; elapsedSeconds = $null; log = "$Name.log" }
    $checks.Add($entry)
    Write-Host "Movie Theater QA: $Name"
    try {
        # Windows PowerShell treats native stderr as ErrorRecords. Let the native process finish cleanup;
        # determine failure from its exit code instead of terminating it on the first stderr line.
        $previousErrorPreference = $ErrorActionPreference
        try {
            $ErrorActionPreference = 'Continue'
            & $runtime @CommandArguments 2>&1 | Tee-Object -FilePath (Join-Path $runDirectory "$Name.log")
            $nativeExitCode = $LASTEXITCODE
        }
        finally { $ErrorActionPreference = $previousErrorPreference }
        $entry.exitCode = $nativeExitCode
        if ($nativeExitCode -ne 0) { throw "$Name failed with exit code $nativeExitCode. See $runDirectory" }
        $entry.status = 'passed'
    }
    catch { $entry.status = 'failed'; throw }
    finally { $entry.elapsedSeconds = [Math]::Round($clock.Elapsed.TotalSeconds, 2) }
}

Push-Location -LiteralPath $repositoryRoot
try {
    Invoke-TheaterCheck 'build' @('build', 'AniT.slnx', '-c', 'Release', '--no-restore')
    Invoke-TheaterCheck 'functional' @('test', 'tests/AniT.Tests', '-c', 'Release', '--no-build', '--no-restore', '--results-directory', (Join-Path $runDirectory 'tests'), '--logger', 'trx;LogFileName=functional.trx', '--logger', 'console;verbosity=minimal')
    $uiChecks = Join-Path $repositoryRoot 'tests/AniT.UiChecks/bin/Release/net10.0-windows/AniT.UiChecks.dll'
    Invoke-TheaterCheck 'players-impaired-network' @($uiChecks, '--check-theater-player')
    Invoke-TheaterCheck 'visual-light-dark' @($uiChecks, '--check-theater-only')
    Invoke-TheaterCheck 'floating-preparation-voice' @($uiChecks, '--check-theater-floating')
    $report.status = 'passed'
}
catch { $report.status = 'failed'; throw }
finally {
    $report.completedUtc = [DateTime]::UtcNow.ToString('o')
    $report | ConvertTo-Json -Depth 6 | Set-Content -LiteralPath (Join-Path $runDirectory 'results.json') -Encoding UTF8
    Pop-Location
    Write-Host "QA report: $runDirectory"
}
