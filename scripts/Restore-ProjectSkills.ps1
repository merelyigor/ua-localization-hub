param(
    [Parameter(Mandatory)] [string]$Repository,
    [string]$InstallerPath = ''
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
if ($Repository -notmatch '^[a-zA-Z0-9_.-]+/[a-zA-Z0-9_.-]+$') { throw 'Expected owner/repository.' }
& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Check-ProjectSkills.ps1')
if ($LASTEXITCODE -ne 0) { throw 'Existing skill imports or manifest are invalid; nothing overwritten.' }
$lock = Get-Content -LiteralPath (Join-Path $root 'docs/ai-workflow/skills.lock.json') -Raw | ConvertFrom-Json
$destination = Join-Path $root '.agents/skills'
$missing = @($lock.skills | Where-Object { -not (Test-Path -LiteralPath (Join-Path $destination $_.name)) })
if ($missing.Count -eq 0) {
    Write-Output 'Selected skills already verified; nothing changed.'
    exit 0
}
if (-not $InstallerPath) {
    $codexDirectory = if ($env:CODEX_HOME) { $env:CODEX_HOME } else { Join-Path ([Environment]::GetFolderPath('UserProfile')) '.codex' }
    $InstallerPath = Join-Path $codexDirectory 'skills/.system/skill-installer/scripts/install-skill-from-github.py'
}
if (-not (Test-Path -LiteralPath $InstallerPath -PathType Leaf)) { throw 'Codex Skill Installer is required; pass -InstallerPath explicitly.' }
function Assert-LocalDirectory([string]$Path) {
    $cursor = [IO.Path]::GetFullPath($Path)
    $prefix = [IO.Path]::TrimEndingDirectorySeparator($root) + [IO.Path]::DirectorySeparatorChar
    if (-not $cursor.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) { throw 'Skill staging escapes repository.' }
    while (-not $cursor.Equals($root, [StringComparison]::OrdinalIgnoreCase)) {
        try {
            if (([IO.File]::GetAttributes($cursor) -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw 'Reparse point in skill staging/promotion path.'
            }
        } catch [IO.FileNotFoundException] { } catch [IO.DirectoryNotFoundException] { }
        $cursor = [IO.Path]::GetDirectoryName($cursor)
    }
}
# До повної перевірки source не потрапляє до discoverable .agents/skills.
$stage = Join-Path $root ('artifacts/skill-stage-' + [guid]::NewGuid().ToString('N'))
Assert-LocalDirectory $stage
$stageLock = Join-Path $stage 'docs/ai-workflow'
New-Item -ItemType Directory -Path $stageLock -Force | Out-Null
$stagedLock = [ordered]@{ schema_version = $lock.schema_version; source_revision = $lock.source_revision; skills = $missing }
$stagedLock | ConvertTo-Json -Depth 12 | Set-Content -LiteralPath (Join-Path $stageLock 'skills.lock.json') -Encoding utf8
$stagedSkills = Join-Path $stage '.agents/skills'
$arguments = @('-X', 'utf8', '-B', $InstallerPath, '--repo', $Repository, '--ref', $lock.source_revision,
    '--method', 'git', '--dest', $stagedSkills, '--path') + @($missing.source_path)
& python @arguments
if ($LASTEXITCODE -ne 0) { throw 'Skill installation failed in ignored staging; no discoverable instructions were installed.' }
& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Check-ProjectSkills.ps1') -RepositoryRoot $stage -RequireInstalled
if ($LASTEXITCODE -ne 0) { throw 'Staged skills did not match pinned resources; nothing promoted.' }
Assert-LocalDirectory $destination
if (-not (Test-Path -LiteralPath $destination)) {
    New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
    New-Item -ItemType Directory -Path $destination | Out-Null
}
foreach ($skill in $missing) {
    $source = Join-Path $stagedSkills $skill.name
    $target = Join-Path $destination $skill.name
    if (Test-Path -LiteralPath $target) { throw "Skill destination changed during staging; nothing overwritten: $($skill.name)" }
    Assert-LocalDirectory $target
    [IO.Directory]::Move($source, $target) # Add-only promotion; a failed run can safely resume.
}
& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Check-ProjectSkills.ps1') -RequireInstalled
if ($LASTEXITCODE -ne 0) { throw 'Downloaded skills did not match pinned resources.' }
Write-Output 'Selected project-local skills restored without replacing existing imports.'
exit 0
