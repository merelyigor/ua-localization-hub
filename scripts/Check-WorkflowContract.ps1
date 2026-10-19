param([string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot))

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$files = @('AGENTS.md', 'docs/ai-workflow/README.md', 'docs/ai-workflow/WORKFLOW.md',
    'docs/ai-workflow/ROLES.md', 'docs/ai-workflow/REVIEW.md', 'docs/ai-workflow/PROMPTS.md',
    'docs/ai-workflow/HANDOFF.md', 'docs/ai-workflow/PROJECT_CHAT_RULES.md',
    'docs/ai-workflow/DOMAIN_RULES.md', 'docs/ai-workflow/SKILLS.md', 'docs/releases/README.md',
    'scripts/Select-CiDotNetValidation.ps1', 'scripts/Test-Select-CiDotNetValidation.ps1')

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

# Звіряємо перенесені contracts із зафіксованими paragraphs, а не лише headings.
$preservation = Get-Content -LiteralPath (Join-Path $root 'docs/ai-workflow/rules-preservation.json') -Raw | ConvertFrom-Json
if ($preservation.schema_version -ne 1 -or $preservation.sections.Count -ne 43 -or
    $preservation.source_commit -notmatch '^[a-f0-9]{40}$') { throw 'Invalid rule preservation manifest.' }
$boundIds = @()
foreach ($binding in $preservation.sections) {
    if ($binding.section -notin (1..43) -or $boundIds -contains $binding.section -or
        $binding.sha256 -notmatch '^[a-f0-9]{64}$') { throw 'Invalid or duplicate rule section binding.' }
    $boundIds += $binding.section
}
$domain = [IO.File]::ReadAllText((Join-Path $root 'docs/ai-workflow/DOMAIN_RULES.md')).Replace("`r`n", "`n")
$rootText = $agents.Replace("`r`n", "`n")
$domainSections = @{}
foreach ($part in [regex]::Matches($domain, '(?ms)^<a id="section-(\d+)"></a>\n\n(.*?)(?=^<a id="section-|\z)')) {
    $id = [int]$part.Groups[1].Value
    if ($domainSections.ContainsKey($id)) { throw 'Duplicate domain section.' }
    $domainSections[$id] = $part.Groups[2].Value
}
$rootSections = @{}
$heads = [regex]::Matches($rootText, '(?m)^## §(\d+) .*$')
for ($i = 0; $i -lt $heads.Count; $i++) {
    $end = if ($i + 1 -lt $heads.Count) { $heads[$i + 1].Index } else { $rootText.Length }
    $id = [int]$heads[$i].Groups[1].Value
    if ($rootSections.ContainsKey($id)) { throw 'Duplicate root section.' }
    $rootSections[$id] = $rootText.Substring($heads[$i].Index, $end - $heads[$i].Index)
}
function Get-RuleHash([string]$Text) {
    $normalized = $Text.Replace('](../../', '](').Trim() + "`n"
    return [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($normalized))).ToLowerInvariant()
}
foreach ($section in $preservation.sections) {
    $id = [int]$section.section
    if (-not $domainSections.ContainsKey($id) -or -not $rootSections.ContainsKey($id) -or
        (Get-RuleHash $domainSections[$id]) -cne $section.sha256) { throw "Rule preservation failed: section $id" }
    if ($id -eq 41 -and $section.source_sha256) {
        $historical = $domainSections[$id].Replace('docs/plans/archive/client-self-update.md', 'docs/plans/active/client-self-update.md')
        if ((Get-RuleHash $historical) -cne $section.source_sha256) { throw 'Self-update contract changed beyond the archived reference correction.' }
    }
    if ($id -in $preservation.retained_sections -or $id -eq 34) {
        if ((Get-RuleHash $rootSections[$id]) -cne $section.sha256) { throw "Root safety/commit rule differs from preserved contract: $id" }
    } elseif (-not $rootSections[$id].Contains("docs/ai-workflow/DOMAIN_RULES.md#section-$id")) {
        throw "Missing conditional route for section $id"
    }
}
& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Check-ProjectSkills.ps1') -RepositoryRoot $root
if ($LASTEXITCODE -ne 0) { throw 'Project skill integrity validation failed.' }
Write-Output "Workflow contract passed; AGENTS.md $bytes bytes / $budget budget."
exit 0
