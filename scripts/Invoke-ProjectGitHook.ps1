param([Parameter(Mandatory)][ValidateSet('pre-commit', 'pre-push')][string]$Hook)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
Push-Location $root
try {
    if ($Hook -eq 'pre-commit') {
        $paths = @(& git -c core.quotepath=false diff --cached --name-only --diff-filter=ACMRT)
        if ($LASTEXITCODE -ne 0) { throw 'Could not enumerate staged paths.' }
        if ($paths.Count -eq 0) {
            Write-Output 'Pre-commit: no staged changes.'
            exit 0
        }
        & git diff --cached --check
        if ($LASTEXITCODE -ne 0) { throw 'Staged whitespace check failed.' }
        # Let the safety scanner enumerate the repository itself. Passing a
        # PowerShell array to a native process can flatten it into positional
        # arguments on Windows and accidentally bind a path as RepositoryRoot.
        & pwsh -NoProfile -File (Join-Path $PSScriptRoot 'Check-RepositorySafety.ps1') -RepositoryRoot $root
        if ($LASTEXITCODE -ne 0) { throw 'Repository safety scan failed.' }
        $planChecker = '.agents/skills/dev-standard/scripts/plan_check.py'
        if (Test-Path -LiteralPath $planChecker -PathType Leaf) {
            & python -X utf8 -B $planChecker --root $root
            if ($LASTEXITCODE -ne 0) { throw 'DevStandard execution-plan validation failed.' }
        }
        Write-Output 'Pre-commit checks passed; no files were auto-formatted or changed.'
        exit 0
    }

    $updates = [Collections.Generic.List[object]]::new()
    while (($line = [Console]::In.ReadLine()) -ne $null) {
        if ([string]::IsNullOrWhiteSpace($line)) { continue }
        $parts = @($line -split '\s+')
        if ($parts.Count -ne 4) { throw 'Malformed pre-push ref input; refusing to skip validation.' }
        if ($parts[1] -notmatch '^0+$') {
            $updates.Add([pscustomobject]@{ localSha = $parts[1]; remoteSha = $parts[3] })
        }
    }
    if ($updates.Count -eq 0) {
        Write-Output 'Pre-push: no local commits are being pushed.'
        exit 0
    }

    $profile = 'Docs'
    foreach ($update in $updates) {
        $scopeFile = Join-Path ([IO.Path]::GetTempPath()) ('bdo-ci-scope-' + [guid]::NewGuid().ToString('N') + '.txt')
        try {
            & pwsh -NoProfile -File scripts/Select-CiDotNetValidation.ps1 -EventName push `
                -BaseSha $update.remoteSha -HeadSha $update.localSha -OutputPath $scopeFile
            if ($LASTEXITCODE -ne 0) { throw 'Could not classify the push; refusing to skip full validation.' }
            $choice = [IO.File]::ReadAllText($scopeFile).Trim()
            if ($choice -ne 'run_dotnet=false') { $profile = 'Code'; break }
        } finally {
            if (Test-Path -LiteralPath $scopeFile -PathType Leaf) { Remove-Item -LiteralPath $scopeFile }
        }
    }

    Write-Output "Pre-push: running local $profile validation. Hosted exact-SHA CI remains required."
    & pwsh -NoProfile -File scripts/Invoke-ProjectValidation.ps1 -Profile $profile
    if ($LASTEXITCODE -ne 0) { throw "Local $profile validation failed; push blocked." }
    exit 0
} finally {
    Pop-Location
}
