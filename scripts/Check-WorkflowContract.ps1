param([string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot))

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$files = @('AGENTS.md', 'docs/ai-workflow/README.md', 'docs/ai-workflow/WORKFLOW.md',
    'docs/ai-workflow/ROLES.md', 'docs/ai-workflow/REVIEW.md', 'docs/ai-workflow/PROMPTS.md',
    'docs/ai-workflow/HANDOFF.md', 'docs/ai-workflow/PROJECT_CHAT_RULES.md', 'docs/releases/README.md')

foreach ($file in $files) {
    $path = Join-Path $root $file
    if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing workflow file: $file" }
    $body = [IO.File]::ReadAllText($path)
    foreach ($link in [regex]::Matches($body, '\]\(([^)]+)\)')) {
        $target = ($link.Groups[1].Value -split '#', 2)[0]
        if (-not $target -or $target -match '^[a-z]+:') { continue }
        if (-not (Test-Path -LiteralPath (Join-Path (Split-Path $path -Parent) $target))) {
            throw "Broken workflow link in ${file}: $target"
        }
    }
}

$agents = [IO.File]::ReadAllText((Join-Path $root 'AGENTS.md'))
$workflow = [IO.File]::ReadAllText((Join-Path $root 'docs/ai-workflow/WORKFLOW.md'))
$release = [IO.File]::ReadAllText((Join-Path $root 'docs/releases/README.md'))
foreach ($marker in @('CODEX-OWNED', 'INTERNAL REVIEW COMPLETE')) {
    if (-not $agents.Contains($marker)) { throw "AGENTS.md lacks current policy: $marker" }
}
foreach ($marker in @('-Profile Docs', '-Profile Code', 'exact-SHA', 'BLOCKER/IMPORTANT')) {
    if (-not $workflow.Contains($marker)) { throw "Workflow lacks required gate: $marker" }
}
if ($release.Contains('Production GitHub Releases завжди публікуються вручну власником репозиторію.')) {
    throw 'Release contract still requires the superseded manual-only publication flow.'
}
if (-not $release.Contains('publication intent') -or -not $release.Contains('PUBLIC VERIFIED')) {
    throw 'Release authority/live verification contract is missing.'
}

$config = [IO.File]::ReadAllText((Join-Path $root '.codex/config.toml'))
$budgetMatch = [regex]::Match($config, '(?m)^project_doc_max_bytes\s*=\s*(\d+)\s*$')
if (-not $budgetMatch.Success) { throw 'Missing project instruction byte budget.' }
$budget = [long]$budgetMatch.Groups[1].Value
$bytes = (Get-Item -LiteralPath (Join-Path $root 'AGENTS.md')).Length
if ($bytes -gt $budget) { throw "AGENTS.md ($bytes bytes) exceeds configured budget ($budget)." }
Write-Output "Workflow contract passed; AGENTS.md $bytes bytes / $budget budget."
