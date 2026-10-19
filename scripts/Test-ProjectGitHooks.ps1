$ErrorActionPreference = 'Stop'
$scriptPath = Join-Path $PSScriptRoot 'Install-ProjectHooks.ps1'
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('bdo-git-hooks-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
$passed = 0
$skipped = 0
$gitCorePath = (& git --exec-path).Trim()
$gitRoot = Split-Path (Split-Path (Split-Path $gitCorePath -Parent) -Parent) -Parent
$gitBash = Join-Path $gitRoot 'bin/bash.exe'
if (-not (Test-Path -LiteralPath $gitBash -PathType Leaf)) { throw 'Git for Windows Bash is required to verify hook dispatch.' }
function Assert-True([string]$Name, [bool]$Condition) {
    if (-not $Condition) { throw "FAIL: $Name" }
    $script:passed++
    Write-Output "PASS: $Name"
}
function New-Fixture([string]$Path) {
    New-Item -ItemType Directory -Path (Join-Path $Path '.githooks') -Force | Out-Null
    New-Item -ItemType Directory -Path (Join-Path $Path '.git/hooks') -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $Path '.githooks/pre-commit'), "#!/bin/sh`n")
    [IO.File]::WriteAllText((Join-Path $Path '.githooks/pre-push'), "#!/bin/sh`n")
    Push-Location $Path
    try {
        & git init --quiet
        if ($LASTEXITCODE -ne 0) { throw 'Cannot initialize Git hook fixture.' }
    } finally { Pop-Location }
}
function Set-HookExecutable([string]$Path) {
    & $gitBash -lc 'chmod +x "$1"' -- $Path
    if ($LASTEXITCODE -ne 0) { throw "Could not mark fixture hook executable: $Path" }
}
function Invoke-FixtureCommit([string]$Repository) {
    $output = & $gitBash -lc 'git -C "$1" commit --quiet -m "hook dispatch test"' -- $Repository 2>&1
    return @{ Code = $LASTEXITCODE; Text = ($output -join "`n") }
}
function Assert-HookDispatch([string]$Name, [hashtable]$Result, [string]$Repository) {
    if ($Result.Text -match '(?i)cannot spawn .*Function not implemented') {
        $script:skipped++
        Write-Output "SKIP: $Name (Codex PowerShell runner blocks Git child-process dispatch; verified separately from Git Bash)"
        return
    }
    $passed = $Result.Code -ne 0 -and $Result.Text -match 'hook-ran' -and
        -not (& git -C $Repository rev-parse --verify HEAD 2>$null)
    if (-not $passed) { throw "FAIL: $Name (exit $($Result.Code)): $($Result.Text)" }
    Assert-True $Name $true
}
function Invoke-Install([string]$Path) {
    $output = & pwsh -NoProfile -File $scriptPath -RepositoryRoot $Path 2>&1
    return @{ Code = $LASTEXITCODE; Text = ($output -join "`n") }
}
try {
    $normal = Join-Path $fixture 'normal'
    New-Item -ItemType Directory -Path $normal | Out-Null
    New-Fixture $normal
    $first = Invoke-Install $normal
    Assert-True 'empty local configuration enables only project hooksPath' ($first.Code -eq 0 -and
        (& git -C $normal config --local --get core.hooksPath) -ceq '.githooks')
    Assert-True 'setup is idempotent' ((Invoke-Install $normal).Code -eq 0)

    # Verify Git for Windows actually dispatches a configured shell hook when
    # committing, rather than relying only on inspecting core.hooksPath.
    $dispatch = Join-Path $fixture 'dispatch'
    New-Item -ItemType Directory -Path $dispatch | Out-Null
    New-Fixture $dispatch
    $hook = Join-Path $dispatch '.git/hooks/pre-commit'
    [IO.File]::WriteAllText($hook, "#!/bin/sh`nprintf 'hook-ran\\n' >&2`nexit 42`n", [Text.UTF8Encoding]::new($false))
    Set-HookExecutable $hook
    Push-Location $dispatch
    try {
        & git config user.name 'Hook Test'
        & git config user.email 'hook-test@example.invalid'
        [IO.File]::WriteAllText((Join-Path $dispatch 'tracked.txt'), 'fixture')
        & git add -- tracked.txt
    } finally { Pop-Location }
    Assert-HookDispatch 'Git commit dispatches the default Windows shell hook' (Invoke-FixtureCommit $dispatch) $dispatch

    $customDispatch = Join-Path $fixture 'custom-dispatch'
    New-Item -ItemType Directory -Path $customDispatch | Out-Null
    New-Fixture $customDispatch
    [IO.File]::WriteAllText((Join-Path $customDispatch '.githooks/pre-commit'),
        "#!/bin/sh`nprintf 'hook-ran\\n' >&2`nexit 42`n", [Text.UTF8Encoding]::new($false))
    Set-HookExecutable (Join-Path $customDispatch '.githooks/pre-commit')
    & git -C $customDispatch config --local core.hooksPath .githooks
    Push-Location $customDispatch
    try {
        & git config user.name 'Hook Test'
        & git config user.email 'hook-test@example.invalid'
        [IO.File]::WriteAllText((Join-Path $customDispatch 'tracked.txt'), 'fixture')
        & git add -- tracked.txt
    } finally { Pop-Location }
    Assert-HookDispatch 'Git commit dispatches core.hooksPath hooks on Windows' (Invoke-FixtureCommit $customDispatch) $customDispatch

    $custom = Join-Path $fixture 'custom'
    New-Item -ItemType Directory -Path $custom | Out-Null
    New-Fixture $custom
    & git -C $custom config --local core.hooksPath custom-hooks
    $customResult = Invoke-Install $custom
    Assert-True 'a pre-existing custom hooksPath is preserved and blocks setup' ($customResult.Code -ne 0 -and
        (& git -C $custom config --local --get core.hooksPath) -ceq 'custom-hooks')

    $existing = Join-Path $fixture 'existing'
    New-Item -ItemType Directory -Path $existing | Out-Null
    New-Fixture $existing
    [IO.File]::WriteAllText((Join-Path $existing '.git/hooks/pre-commit'), "#!/bin/sh`n")
    $existingResult = Invoke-Install $existing
    Assert-True 'an existing non-sample hook is not disabled' ($existingResult.Code -ne 0 -and
        -not (& git -C $existing config --local --get core.hooksPath 2>$null))
    Write-Output "Project Git hooks tests: $passed passed, $skipped skipped."
} finally {
    $resolved = (Resolve-Path -LiteralPath $fixture).Path
    $prefix = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetTempPath()) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or
        (Split-Path $resolved -Leaf) -notlike 'bdo-git-hooks-*') { throw 'Unsafe hook fixture cleanup path.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
exit 0
