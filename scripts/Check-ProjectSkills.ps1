param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [switch]$RequireInstalled
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $RepositoryRoot).Path
$lock = Get-Content -LiteralPath (Join-Path $root 'docs/ai-workflow/skills.lock.json') -Raw | ConvertFrom-Json
if ($lock.schema_version -ne 1 -or $lock.source_revision -notmatch '^[a-f0-9]{40}$' -or $lock.skills.Count -eq 0) {
    throw 'Invalid skill lock manifest.'
}
$names = @()
foreach ($skill in $lock.skills) {
    if ($skill.name -notmatch '^[a-z0-9]+(?:-[a-z0-9]+)*$' -or $names -contains $skill.name -or
        $skill.source_path -cne "plugins/merely/skills/$($skill.name)" -or $skill.files.Count -eq 0) {
        throw 'Invalid or duplicate skill identity.'
    }
    $names += $skill.name
    $paths = @()
    foreach ($file in $skill.files) {
        if ($file.path -notmatch '^[a-zA-Z0-9_.-]+(?:/[a-zA-Z0-9_.-]+)*$' -or
            @($file.path.Split('/') | Where-Object { $_ -in @('.', '..') }).Count -ne 0 -or
            $paths -contains $file.path -or $file.sha256 -notmatch '^[a-f0-9]{64}$') { throw 'Invalid skill file binding.' }
        $paths += $file.path
    }
    if ($paths -cnotcontains 'SKILL.md') { throw 'Skill has no entrypoint.' }
}

function Assert-NoReparse([string]$Path) {
    $cursor = $Path
    while (-not $cursor.Equals($root, [StringComparison]::OrdinalIgnoreCase)) {
        try {
            if (([IO.File]::GetAttributes($cursor) -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                throw 'Reparse points are not supported in local skill imports.'
            }
        } catch [IO.FileNotFoundException] { } catch [IO.DirectoryNotFoundException] { }
        $cursor = [IO.Path]::GetDirectoryName($cursor)
    }
}

$imports = Join-Path $root '.agents/skills'
Assert-NoReparse $imports
if (Test-Path -LiteralPath $imports) {
    foreach ($entry in Get-ChildItem -LiteralPath $imports -Force) {
        Assert-NoReparse $entry.FullName
        if (-not $entry.PSIsContainer -or $names -cnotcontains $entry.Name) {
            throw "Unexpected entry in skill import root: $($entry.Name)"
        }
    }
}
$present = @($names | Where-Object { Test-Path -LiteralPath (Join-Path $imports $_) })
if ($present.Count -eq 0) {
    if ($RequireInstalled) { throw 'Selected skills are not installed locally.' }
    Write-Output 'Project skills: not installed; public workflow fallback active (CI needs no private access).'
    exit 0
}
if ($RequireInstalled -and $present.Count -ne $names.Count) {
    throw 'Selected project skill set is incomplete.'
}
$count = 0
foreach ($skill in ($lock.skills | Where-Object { $present -ccontains $_.name })) {
    $directory = Join-Path $imports $skill.name
    Assert-NoReparse $directory
    $expected = @($skill.files.path)
    foreach ($entry in Get-ChildItem -LiteralPath $directory -Recurse -Force) {
        Assert-NoReparse $entry.FullName
        if ($entry.PSIsContainer) { continue }
        $relative = [IO.Path]::GetRelativePath($directory, $entry.FullName).Replace('\', '/')
        if ($relative -match '(^|/)__pycache__/[^/]+\.pyc$') { continue }
        if ($expected -cnotcontains $relative) { throw "Unexpected file in imported skill: $($skill.name)/$relative" }
    }
    foreach ($file in $skill.files) {
        $path = Join-Path $directory $file.path
        Assert-NoReparse $path
        if (-not (Test-Path -LiteralPath $path -PathType Leaf)) { throw "Missing imported file: $($skill.name)/$($file.path)" }
        # Git autocrlf не змінює семантику текстових resources; інші bytes не нормалізуються.
        $strictUtf8 = [Text.UTF8Encoding]::new($false, $true)
        $text = $strictUtf8.GetString([IO.File]::ReadAllBytes($path)).Replace("`r`n", "`n")
        $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($text))).ToLowerInvariant()
        if ($hash -cne $file.sha256) { throw "Imported skill differs from pinned source: $($skill.name)/$($file.path)" }
        $count++
    }
}
if ($present.Count -eq $names.Count) {
    Write-Output "Project skills verified: $($names.Count) skills / $count files; source $($lock.source_revision)."
} else {
    Write-Output "Project skills partially restored: $($present.Count) / $($names.Count) skills verified; source $($lock.source_revision)."
}
exit 0
