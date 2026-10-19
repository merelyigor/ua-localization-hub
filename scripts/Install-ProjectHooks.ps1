param([string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot))

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$gitRoot = (& git -C $root rev-parse --show-toplevel 2>$null)
if ($LASTEXITCODE -ne 0 -or -not $gitRoot) { throw 'Project hooks require a Git worktree.' }
$gitRoot = (Resolve-Path -LiteralPath $gitRoot.Trim()).Path
if (-not $gitRoot.Equals($root, [StringComparison]::OrdinalIgnoreCase)) {
    throw 'RepositoryRoot must be the Git worktree root.'
}

$hookFiles = @('pre-commit', 'pre-push')
foreach ($hook in $hookFiles) {
    if (-not (Test-Path -LiteralPath (Join-Path $root ".githooks/$hook") -PathType Leaf)) {
        throw "Missing project hook: .githooks/$hook"
    }
}

$current = (& git -C $root config --local --get core.hooksPath 2>$null)
$hasConfiguredPath = $LASTEXITCODE -eq 0
if ($hasConfiguredPath -and $current.Trim() -cne '.githooks') {
    throw "A different repository-local hooksPath is configured ('$($current.Trim())'); refusing to replace it."
}

$gitHooksPath = (& git -C $root rev-parse --git-path hooks).Trim()
if (-not [IO.Path]::IsPathRooted($gitHooksPath)) { $gitHooksPath = Join-Path $root $gitHooksPath }
$existing = @(Get-ChildItem -LiteralPath $gitHooksPath -File -ErrorAction SilentlyContinue |
    Where-Object { $_.Name -notlike '*.sample' })
if ($existing.Count -gt 0 -and (-not $hasConfiguredPath -or $current.Trim() -cne '.githooks')) {
    throw 'Existing non-sample Git hooks were found; refusing to disable or replace them.'
}

& git -C $root config --local core.hooksPath .githooks
if ($LASTEXITCODE -ne 0) { throw 'Could not set repository-local core.hooksPath.' }
Write-Output 'Repository-local Git hooks enabled at .githooks; hosted CI remains enabled.'
