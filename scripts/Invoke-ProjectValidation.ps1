param(
    [ValidateSet('Docs', 'Code')] [string]$Profile = 'Code'
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$reportDir = Join-Path $root ('artifacts/validation/' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $reportDir -Force | Out-Null
$steps = [Collections.Generic.List[object]]::new()
$report = [ordered]@{ profile = $Profile; source_sha = ''; dirty = $true; result = 'FAIL'; steps = $steps }

function Invoke-Gate([string]$Name, [string]$Executable, [string[]]$CommandArguments) {
    $log = Join-Path $reportDir ($Name + '.log')
    $timer = [Diagnostics.Stopwatch]::StartNew()
    & $Executable @CommandArguments 2>&1 | Tee-Object -FilePath $log | Out-Host
    $code = $LASTEXITCODE
    $steps.Add([ordered]@{ name = $Name; command = $Executable; arguments = $CommandArguments;
        exit_code = $code; elapsed_seconds = [math]::Round($timer.Elapsed.TotalSeconds, 2) })
    if ($code -ne 0) { throw "Gate failed: $Name (exit $code)." }
}

Push-Location $root
try {
    $sha = git rev-parse HEAD
    if ($LASTEXITCODE -ne 0) { throw 'Cannot determine source SHA.' }
    $report.source_sha = $sha
    $status = @(git status --porcelain)
    if ($LASTEXITCODE -ne 0) { throw 'Cannot determine worktree state.' }
    $report.dirty = $status.Count -gt 0
    Invoke-Gate 'workflow' 'pwsh' @('-NoProfile', '-File', 'scripts/Check-WorkflowContract.ps1')
    Invoke-Gate 'portable-rules' 'pwsh' @('-NoProfile', '-File', 'scripts/Check-ProjectChatRules.ps1')
    Invoke-Gate 'safety' 'pwsh' @('-NoProfile', '-File', 'scripts/Check-RepositorySafety.ps1')
    Invoke-Gate 'harness-tests' 'pwsh' @('-NoProfile', '-File', 'scripts/Test-ProjectHarness.ps1')
    Invoke-Gate 'resolver-tests' 'pwsh' @('-NoProfile', '-File', 'scripts/Test-Resolve-ReleaseVersion.ps1')
    Invoke-Gate 'release-note-tests' 'pwsh' @('-NoProfile', '-File', 'scripts/Test-Generate-ReleaseNotes.ps1')
    # Synthetic identity: це перевірка NEXT schema/render, а не resolution нового релізу.
    Invoke-Gate 'next-render' 'pwsh' @('-NoProfile', '-File', 'scripts/Generate-ReleaseNotes.ps1',
        '-Version', '0.0.0', '-Tag', 'v0.0.0', '-Sha', $sha,
        '-AssetName', 'BDO-WWM-UAClient-v0.0.0-win-x64.zip', '-ExeSha256', ('0' * 64))
    if ($Profile -eq 'Code') {
        Invoke-Gate 'build' 'dotnet' @('build', 'BdoUaClient.sln', '-c', 'Release', '-warnaserror')
        Invoke-Gate 'tests' 'dotnet' @('test', 'BdoUaClient.sln', '-c', 'Release', '--no-build')
    }
    Invoke-Gate 'diff' 'git' @('diff', '--check')
    Invoke-Gate 'staged-diff' 'git' @('diff', '--cached', '--check')
    $report.result = 'PASS'
} catch {
    Write-Host $_.Exception.Message
} finally {
    $report | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $reportDir 'report.json') -Encoding utf8
    Pop-Location
    Write-Output "Validation $($report.result): $reportDir"
}
if ($report.result -ne 'PASS') { exit 1 }
