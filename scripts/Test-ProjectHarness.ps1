$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('bdo-harness-tests-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
$passed = 0

function Assert-True([string]$Name, [bool]$Condition) {
    if (-not $Condition) { throw "FAIL: $Name" }
    $script:passed++
    Write-Output "PASS: $Name"
}
function Invoke-Safety([string]$File) {
    $output = & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Check-RepositorySafety.ps1') -RepositoryRoot $fixture -Paths $File 2>&1
    return @{ Code = $LASTEXITCODE; Text = ($output -join "`n") }
}
try {
    [IO.File]::WriteAllText((Join-Path $fixture 'safe.md'), '# Synthetic documentation')
    Assert-True 'safe text accepted' ((Invoke-Safety 'safe.md').Code -eq 0)
    $fakeSecret = 'gh' + 'p_' + ('A' * 36)
    [IO.File]::WriteAllText((Join-Path $fixture 'unsafe.md'), $fakeSecret)
    $unsafe = Invoke-Safety 'unsafe.md'
    Assert-True 'credential signature rejected' ($unsafe.Code -ne 0)
    Assert-True 'credential bytes never printed' (-not $unsafe.Text.Contains($fakeSecret))
    Assert-True 'private config filename rejected' ((Invoke-Safety 'config.json').Code -ne 0)
    Assert-True 'generated binary filename rejected' ((Invoke-Safety 'artifacts/output.exe').Code -ne 0)
    Assert-True 'review patch filename rejected' ((Invoke-Safety 'review.patch').Code -ne 0)
    Assert-True 'private skill source cannot be tracked' ((Invoke-Safety '.agents/skills/private/SKILL.md').Code -ne 0)
    Assert-True 'path escape rejected' ((Invoke-Safety '../outside.md').Code -ne 0)

    $owned = Join-Path $fixture 'owned'
    $outside = Join-Path $fixture 'outside'
    New-Item -ItemType Directory -Path $owned, $outside | Out-Null
    [IO.File]::WriteAllText((Join-Path $outside 'external.md'), 'Synthetic outside content')
    $junction = Join-Path $owned 'linked'
    New-Item -ItemType Junction -Path $junction -Target $outside | Out-Null
    try {
        $linkOutput = & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Check-RepositorySafety.ps1') -RepositoryRoot $owned -Paths 'linked/external.md' 2>&1
        Assert-True 'junction escape rejected before reading external content' ($LASTEXITCODE -ne 0 -and
            ($linkOutput -join "`n").Contains('Reparse point'))
    } finally {
        Remove-Item -LiteralPath $junction -Force
    }

    Copy-Item -LiteralPath (Join-Path $repoRoot 'AGENTS.md') -Destination $fixture
    Copy-Item -LiteralPath (Join-Path $repoRoot 'docs') -Destination $fixture -Recurse
    New-Item -ItemType Directory -Path (Join-Path $fixture '.codex') | Out-Null
    Copy-Item -LiteralPath (Join-Path $repoRoot '.codex/config.toml') -Destination (Join-Path $fixture '.codex/config.toml')
    $checker = Join-Path $PSScriptRoot 'Check-WorkflowContract.ps1'
    & pwsh -NoProfile -File $checker -RepositoryRoot $fixture | Out-Host
    Assert-True 'current policy/links/instruction budget valid' ($LASTEXITCODE -eq 0)
    $domainPath = Join-Path $fixture 'docs/ai-workflow/DOMAIN_RULES.md'
    $originalDomain = [IO.File]::ReadAllText($domainPath)
    [IO.File]::WriteAllText($domainPath, $originalDomain.Replace('§13.10 WWM', '§13.10 Unsafe WWM'))
    & pwsh -NoProfile -File $checker -RepositoryRoot $fixture *> $null
    Assert-True 'changed game safety paragraph fails preservation gate' ($LASTEXITCODE -ne 0)
    [IO.File]::WriteAllText($domainPath, $originalDomain)
    [IO.File]::WriteAllText((Join-Path $fixture '.codex/config.toml'), 'project_doc_max_bytes = 1')
    & pwsh -NoProfile -File $checker -RepositoryRoot $fixture *> $null
    Assert-True 'truncated instruction budget rejected' ($LASTEXITCODE -ne 0)
    Copy-Item -LiteralPath (Join-Path $repoRoot '.codex/config.toml') -Destination (Join-Path $fixture '.codex/config.toml')
    [IO.File]::AppendAllText((Join-Path $fixture 'docs/ai-workflow/README.md'), "`n[broken](missing-file.md)")
    & pwsh -NoProfile -File $checker -RepositoryRoot $fixture *> $null
    Assert-True 'broken workflow link rejected' ($LASTEXITCODE -ne 0)

    # Негативний integration test: failed gate має зупинити runner й записати FAIL.
    New-Item -ItemType Directory -Path (Join-Path $fixture 'scripts') | Out-Null
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Invoke-ProjectValidation.ps1') -Destination (Join-Path $fixture 'scripts')
    [IO.File]::WriteAllText((Join-Path $fixture 'scripts/Check-WorkflowContract.ps1'), "exit 7`n")
    Push-Location $fixture
    try {
        & git init --quiet
        if ($LASTEXITCODE -ne 0) { throw 'Cannot initialize synthetic git fixture.' }
        & git -c user.name=Fixture -c user.email=fixture@example.invalid commit --allow-empty --quiet -m fixture
        if ($LASTEXITCODE -ne 0) { throw 'Cannot commit synthetic empty fixture.' }
    } finally { Pop-Location }
    $scanOutput = & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Check-RepositorySafety.ps1') -RepositoryRoot $fixture 2>&1
    Assert-True 'new untracked source is included in default scan' ($LASTEXITCODE -ne 0 -and
        ($scanOutput -join "`n").Contains('unsafe.md'))
    & pwsh -NoProfile -File (Join-Path $fixture 'scripts/Invoke-ProjectValidation.ps1') -Profile Docs | Out-Host
    Assert-True 'failed gate returns failure from runner' ($LASTEXITCODE -ne 0)
    $reports = @(Get-ChildItem -LiteralPath (Join-Path $fixture 'artifacts/validation') -Filter report.json -Recurse)
    $report = [IO.File]::ReadAllText($reports[0].FullName) | ConvertFrom-Json
    Assert-True 'failure report records exact exit and stops later gates' ($report.result -eq 'FAIL' -and
        $report.steps.Count -eq 1 -and $report.steps[0].exit_code -eq 7)
    & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Test-ProjectSkills.ps1') | Out-Host
    Assert-True 'project skill integration regression suite' ($LASTEXITCODE -eq 0)
    Write-Output "Harness tests: $passed passed."
} finally {
    $resolved = (Resolve-Path -LiteralPath $fixture).Path
    $tempPrefix = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetTempPath()) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($tempPrefix, [StringComparison]::OrdinalIgnoreCase) -or
        (Split-Path $resolved -Leaf) -notlike 'bdo-harness-tests-*') { throw 'Unsafe fixture cleanup path.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
# GitHub Actions додає exit-code epilogue навіть після успішних assertions.
# Очікувана помилка дочірнього процесу не є результатом цього скрипту.
exit 0
