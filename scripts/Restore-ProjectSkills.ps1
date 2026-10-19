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
if (@($lock.skills | Where-Object { Test-Path -LiteralPath (Join-Path $destination $_.name) }).Count -eq $lock.skills.Count) {
    Write-Output 'Selected skills already verified; nothing changed.'
    exit 0
}
if (-not $InstallerPath) {
    $codexDirectory = if ($env:CODEX_HOME) { $env:CODEX_HOME } else { Join-Path ([Environment]::GetFolderPath('UserProfile')) '.codex' }
    $InstallerPath = Join-Path $codexDirectory 'skills/.system/skill-installer/scripts/install-skill-from-github.py'
}
if (-not (Test-Path -LiteralPath $InstallerPath -PathType Leaf)) { throw 'Codex Skill Installer is required; pass -InstallerPath explicitly.' }
if ((Test-Path -LiteralPath $destination) -and @(Get-ChildItem -LiteralPath $destination -Force).Count -ne 0) {
    throw 'Skill destination contains unrelated files; nothing overwritten.'
}
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
Copy-Item -LiteralPath (Join-Path $root 'docs/ai-workflow/skills.lock.json') -Destination $stageLock
$stagedSkills = Join-Path $stage '.agents/skills'
$arguments = @('-X', 'utf8', '-B', $InstallerPath, '--repo', $Repository, '--ref', $lock.source_revision,
    '--method', 'git', '--dest', $stagedSkills, '--path') + @($lock.skills.source_path)
& python @arguments
if ($LASTEXITCODE -ne 0) { throw 'Skill installation failed in ignored staging; no discoverable instructions were installed.' }
& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Check-ProjectSkills.ps1') -RepositoryRoot $stage -RequireInstalled
if ($LASTEXITCODE -ne 0) { throw 'Staged skills did not match pinned resources; nothing promoted.' }
Assert-LocalDirectory $destination
if (Test-Path -LiteralPath $destination) {
    if (@(Get-ChildItem -LiteralPath $destination -Force).Count -ne 0) { throw 'Destination changed during staging; nothing overwritten.' }
    [IO.Directory]::Delete($destination) # Лише порожня тека, без recursive delete.
}
New-Item -ItemType Directory -Path (Split-Path $destination -Parent) -Force | Out-Null
[IO.Directory]::Move($stagedSkills, $destination) # Atomic same-volume promotion; без merge/overwrite.
& pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Check-ProjectSkills.ps1') -RequireInstalled
if ($LASTEXITCODE -ne 0) { throw 'Downloaded skills did not match pinned resources.' }
Write-Output 'Selected project-local skills restored; available to subsequent Codex turns/sessions.'
exit 0
