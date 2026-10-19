$ErrorActionPreference = 'Stop'
$checker = Join-Path $PSScriptRoot 'Check-ProjectSkills.ps1'
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('bdo-skill-tests-' + [guid]::NewGuid().ToString('N'))
$lockPath = Join-Path $fixture 'docs/ai-workflow/skills.lock.json'
$directory = Join-Path $fixture '.agents/skills/fixture-skill'
$passed = 0
function Assert-True([string]$Name, [bool]$Condition) {
    if (-not $Condition) { throw "FAIL: $Name" }
    $script:passed++
    Write-Output "PASS: $Name"
}
function Invoke-Check([switch]$Required) {
    $arguments = @('-NoProfile', '-File', $checker, '-RepositoryRoot', $fixture)
    if ($Required) { $arguments += '-RequireInstalled' }
    & pwsh @arguments *> $null
    return $LASTEXITCODE
}
try {
    New-Item -ItemType Directory -Path (Split-Path $lockPath -Parent) -Force | Out-Null
    $content = "# Synthetic skill`n"
    $hash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($content))).ToLowerInvariant()
    $lock = @{ schema_version = 1; source_revision = ('a' * 40); skills = @(@{
        name = 'fixture-skill'; source_path = 'plugins/merely/skills/fixture-skill';
        files = @(@{ path = 'SKILL.md'; sha256 = $hash })
    }) }
    $lock | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $lockPath -Encoding utf8
    Assert-True 'CI without private imports uses explicit public fallback' ((Invoke-Check) -eq 0)
    Assert-True 'required local imports fail when absent' ((Invoke-Check -Required) -ne 0)
    New-Item -ItemType Directory -Path $directory -Force | Out-Null
    [IO.File]::WriteAllText((Join-Path $directory 'SKILL.md'), $content)
    Assert-True 'exact synthetic skill accepted' ((Invoke-Check -Required) -eq 0)
    [IO.File]::WriteAllText((Join-Path $directory 'SKILL.md'), "# Changed`n")
    Assert-True 'modified skill hash rejected' ((Invoke-Check) -ne 0)
    [IO.File]::WriteAllText((Join-Path $directory 'SKILL.md'), $content, [Text.Encoding]::Unicode)
    Assert-True 'unexpected UTF16 encoding cannot bypass resource hash' ((Invoke-Check) -ne 0)
    [IO.File]::WriteAllText((Join-Path $directory 'SKILL.md'), $content.Replace("`n", "`r`n"))
    Assert-True 'Git CRLF conversion preserves content identity' ((Invoke-Check) -eq 0)
    [IO.File]::WriteAllText((Join-Path $directory 'unexpected.md'), 'unexpected')
    Assert-True 'extra imported resource rejected' ((Invoke-Check) -ne 0)
    Remove-Item -LiteralPath (Join-Path $directory 'unexpected.md')
    $lock.skills[0].files[0].path = '../SKILL.md'
    $lock | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $lockPath -Encoding utf8
    Assert-True 'manifest traversal rejected' ((Invoke-Check) -ne 0)
    $lock.skills[0].files[0].path = 'SKILL.md'
    $lock.skills += @{ name = 'second-skill'; source_path = 'plugins/merely/skills/second-skill'; files = @(@{ path = 'SKILL.md'; sha256 = $hash }) }
    $lock | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $lockPath -Encoding utf8
    Assert-True 'partial installation cannot silently pass' ((Invoke-Check) -ne 0)
    $lock.skills = @($lock.skills[0], $lock.skills[0])
    $lock | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $lockPath -Encoding utf8
    Assert-True 'duplicate manifest identities rejected' ((Invoke-Check) -ne 0)
    $lock.skills = @($lock.skills[0])
    $lock | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath $lockPath -Encoding utf8
    # Junction веде лише до іншої synthetic теки в цьому ж temp fixture.
    $outside = Join-Path $fixture 'outside'
    New-Item -ItemType Directory -Path $outside | Out-Null
    [IO.File]::WriteAllText((Join-Path $outside 'SKILL.md'), $content)
    Remove-Item -LiteralPath (Join-Path $directory 'SKILL.md')
    Remove-Item -LiteralPath $directory
    New-Item -ItemType Junction -Path $directory -Target $outside | Out-Null
    try { Assert-True 'junction import rejected' ((Invoke-Check) -ne 0) }
    finally { Remove-Item -LiteralPath $directory -Force }
    New-Item -ItemType Directory -Path (Join-Path $fixture 'scripts') | Out-Null
    Copy-Item -LiteralPath $checker -Destination (Join-Path $fixture 'scripts')
    Copy-Item -LiteralPath (Join-Path $PSScriptRoot 'Restore-ProjectSkills.ps1') -Destination (Join-Path $fixture 'scripts')
    $installer = Join-Path $fixture 'synthetic-installer.py'
    $installerText = @'
import argparse
from pathlib import Path
p=argparse.ArgumentParser()
p.add_argument('--repo');p.add_argument('--ref');p.add_argument('--method');p.add_argument('--dest');p.add_argument('--path',nargs='+')
a=p.parse_args()
assert a.repo=='fixture/source' and a.ref=='a'*40 and a.method=='git'
assert a.path==['plugins/merely/skills/fixture-skill']
target=Path(a.dest)/'fixture-skill'
target.mkdir(parents=True)
(target/'SKILL.md').write_bytes(b'# Synthetic skill\n')
(Path(__file__).parent/'installer-called').write_text('once')
'@
    [IO.File]::WriteAllText($installer, $installerText)
    $restore = Join-Path $fixture 'scripts/Restore-ProjectSkills.ps1'
    & pwsh -NoProfile -File $restore -Repository fixture/source -InstallerPath $installer | Out-Host
    Assert-True 'restoration installs exact pinned resources into isolated root' ($LASTEXITCODE -eq 0 -and (Invoke-Check -Required) -eq 0)
    [IO.File]::WriteAllText($installer, "raise RuntimeError('must not run twice')")
    & pwsh -NoProfile -File $restore -Repository fixture/source -InstallerPath $installer | Out-Host
    Assert-True 'verified restoration is idempotent and does not rerun installer' ($LASTEXITCODE -eq 0)
    [IO.File]::WriteAllText((Join-Path $directory 'SKILL.md'), 'local changes')
    & pwsh -NoProfile -File $restore -Repository fixture/source -InstallerPath $installer *> $null
    Assert-True 'restoration rejects modified imports without overwriting' ($LASTEXITCODE -ne 0 -and
        [IO.File]::ReadAllText((Join-Path $directory 'SKILL.md')) -ceq 'local changes')
    Remove-Item -LiteralPath (Join-Path $directory 'SKILL.md')
    Remove-Item -LiteralPath $directory
    $badInstaller = $installerText.Replace("b'# Synthetic skill\n'", "b'wrong source'")
    [IO.File]::WriteAllText($installer, $badInstaller)
    & pwsh -NoProfile -File $restore -Repository fixture/source -InstallerPath $installer *> $null
    Assert-True 'hash mismatch never leaves discoverable instructions' ($LASTEXITCODE -ne 0 -and
        -not (Test-Path -LiteralPath (Join-Path $directory 'SKILL.md')))
    [IO.File]::WriteAllText($installer, $badInstaller + "`nraise SystemExit(3)`n")
    & pwsh -NoProfile -File $restore -Repository fixture/source -InstallerPath $installer *> $null
    Assert-True 'partial failed installer never leaves discoverable instructions' ($LASTEXITCODE -ne 0 -and
        -not (Test-Path -LiteralPath (Join-Path $directory 'SKILL.md')))
    $extraInstaller = $installerText + @'

extra=Path(a.dest)/'unlisted-skill'
extra.mkdir()
(extra/'SKILL.md').write_text('unverified instructions')
'@
    [IO.File]::WriteAllText($installer, $extraInstaller)
    & pwsh -NoProfile -File $restore -Repository fixture/source -InstallerPath $installer *> $null
    Assert-True 'unlisted skill cannot be promoted into discovery' ($LASTEXITCODE -ne 0 -and
        -not (Test-Path -LiteralPath (Join-Path $directory 'SKILL.md')) -and
        -not (Test-Path -LiteralPath (Join-Path $fixture '.agents/skills/unlisted-skill')))
    Write-Output "Project skill tests: $passed passed."
} finally {
    $resolved = (Resolve-Path -LiteralPath $fixture).Path
    $prefix = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetTempPath()) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or
        (Split-Path $resolved -Leaf) -notlike 'bdo-skill-tests-*') { throw 'Unsafe skill fixture cleanup path.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
exit 0
