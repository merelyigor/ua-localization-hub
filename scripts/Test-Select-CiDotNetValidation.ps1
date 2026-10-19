$ErrorActionPreference = 'Stop'
$scriptPath = Join-Path $PSScriptRoot 'Select-CiDotNetValidation.ps1'
$fixture = Join-Path ([IO.Path]::GetTempPath()) ('bdo-ci-scope-' + [guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $fixture | Out-Null
$passed = 0
function Assert-Decision([string]$Name, [bool]$Expected, [string[]]$Paths, [string]$Event = 'push') {
    $output = Join-Path $fixture ([guid]::NewGuid().ToString('N') + '.txt')
    $decisionOutput = & $scriptPath -EventName $Event -UseProvidedPaths -ChangedPaths $Paths -OutputPath $output
    if ($LASTEXITCODE -ne 0) { throw "FAIL: classifier exited $LASTEXITCODE for $Name" }
    $written = [IO.File]::ReadAllText($output).Trim()
    $actual = $written -eq 'run_dotnet=true'
    if ($actual -ne $Expected) { throw "FAIL: $Name; expected full=$Expected, actual=$actual" }
    $script:passed++
    Write-Output "PASS: $Name"
}
function Assert-GitDecision([string]$Name, [string]$Base, [string]$Head, [bool]$Expected) {
    $output = Join-Path $fixture ([guid]::NewGuid().ToString('N') + '.txt')
    Push-Location (Join-Path $fixture 'repo')
    try {
        $decision = & $scriptPath -EventName push -BaseSha $Base -HeadSha $Head -OutputPath $output
        if ($LASTEXITCODE -ne 0) { throw "FAIL: classifier exited $LASTEXITCODE for $Name" }
    } finally { Pop-Location }
    $actual = [IO.File]::ReadAllText($output).Trim() -eq 'run_dotnet=true'
    if ($actual -ne $Expected) { throw "FAIL: $Name; expected full=$Expected, actual=$actual; $decision" }
    $script:passed++
    Write-Output "PASS: $Name"
}
function Assert-GitEnvironmentDecision([string]$Name, [string]$Event, [string]$Base, [string]$Head, [bool]$Expected) {
    $previousEvent = $env:GITHUB_EVENT_NAME
    $previousBase = $env:GITHUB_BASE_SHA
    $previousHead = $env:GITHUB_SHA
    $output = Join-Path $fixture ([guid]::NewGuid().ToString('N') + '.txt')
    try {
        $env:GITHUB_EVENT_NAME = $Event
        $env:GITHUB_BASE_SHA = $Base
        $env:GITHUB_SHA = $Head
        Push-Location (Join-Path $fixture 'repo')
        try {
            $decision = & $scriptPath -OutputPath $output
            if ($LASTEXITCODE -ne 0) { throw "FAIL: classifier exited $LASTEXITCODE for $Name" }
        } finally { Pop-Location }
    } finally {
        $env:GITHUB_EVENT_NAME = $previousEvent
        $env:GITHUB_BASE_SHA = $previousBase
        $env:GITHUB_SHA = $previousHead
    }
    $actual = [IO.File]::ReadAllText($output).Trim() -eq 'run_dotnet=true'
    if ($actual -ne $Expected) { throw "FAIL: $Name; expected full=$Expected, actual=$actual; $decision" }
    $script:passed++
    Write-Output "PASS: $Name"
}
try {
    $repository = Join-Path $fixture 'repo'
    New-Item -ItemType Directory -Path (Join-Path $repository 'docs') -Force | Out-Null
    Push-Location $repository
    try {
        & git init --quiet
        & git config user.name 'CI scope fixture'
        & git config user.email 'ci-scope@example.invalid'
        [IO.File]::WriteAllText((Join-Path $repository 'docs/README.md'), 'base')
        & git add docs/README.md
        & git commit --quiet -m base
        $base = (& git rev-parse HEAD).Trim()
        [IO.File]::WriteAllText((Join-Path $repository 'docs/README.md'), 'docs change')
        & git commit --quiet -am docs
        $docsHead = (& git rev-parse HEAD).Trim()
        [IO.File]::WriteAllText((Join-Path $repository 'App.cs'), 'class App {}')
        & git add App.cs
        & git commit --quiet -m app
        $appHead = (& git rev-parse HEAD).Trim()
    } finally { Pop-Location }
    Assert-GitDecision 'Git docs-only comparison selects fast CI' $base $docsHead $false
    Assert-GitDecision 'Git mixed/docs-plus-code comparison selects full CI' $base $appHead $true
    Assert-GitDecision 'unresolvable Git base falls back to full CI' ('f' * 40) $appHead $true
    Assert-GitEnvironmentDecision 'GitHub push base SHA selects docs-only fast CI' 'push' $base $docsHead $false
    Assert-GitEnvironmentDecision 'GitHub pull request base SHA selects docs-only fast CI' 'pull_request' $base $docsHead $false
    Assert-Decision -Name 'Markdown, release JSON and documentation image changes skip .NET tests' -Expected $false -Paths @('docs/development/CURRENT.md', 'docs/releases/NEXT.json', 'docs/design/diagram.png', 'README.md')
    Assert-Decision -Name 'agent rules-only changes skip .NET build/tests' -Expected $false -Paths @('AGENTS.md', '.codex/config.toml')
    Assert-Decision -Name 'release docs retain fast validation path' -Expected $false -Paths @('docs/releases/NEXT.json')
    Assert-Decision -Name 'known root documentation files use fast validation' -Expected $false -Paths @('AGENTS.md', 'README.md', 'LICENSE.md', '.codex/config.toml')
    Assert-Decision -Name 'C# source requires full validation' -Expected $true -Paths @('BdoClient/MainForm.cs')
    Assert-Decision -Name 'test changes require full validation' -Expected $true -Paths @('BdoClient.Tests/SmokeTests.cs')
    Assert-Decision -Name 'project/build changes require full validation' -Expected $true -Paths @('BdoClient.csproj')
    Assert-Decision -Name 'workflow and scripts require full validation' -Expected $true -Paths @('.github/workflows/ci.yml', 'scripts/Check-WorkflowContract.ps1')
    Assert-Decision -Name 'git attributes and ignore rules require full validation' -Expected $true -Paths @('.gitattributes', '.gitignore')
    Assert-Decision -Name 'unlisted Codex configuration requires full validation' -Expected $true -Paths @('.codex/hooks/custom.ps1')
    Assert-Decision -Name 'mixed docs and code changes require full validation' -Expected $true -Paths @('docs/README.md', 'BdoClient/Service.cs')
    Assert-Decision -Name 'unknown paths fail safe to full validation' -Expected $true -Paths @('new-root-config.data')
    Assert-Decision -Name 'case-variant documentation path fails safe' -Expected $true -Paths @('DOCS/Directory.Build.props')
    Assert-Decision -Name 'code or unknown files placed inside docs still require full validation' -Expected $true -Paths @('docs/snippet.cs', 'docs/build.props')
    Assert-Decision -Name 'empty or undetermined change set requires full validation' -Expected $true -Paths @()
    Assert-Decision -Name 'manual workflow run always requires full validation' -Expected $true -Paths @('docs/README.md') -Event workflow_dispatch
    Write-Output "CI validation scope tests: $passed passed."
} finally {
    $resolved = (Resolve-Path -LiteralPath $fixture).Path
    $prefix = [IO.Path]::TrimEndingDirectorySeparator([IO.Path]::GetTempPath()) + [IO.Path]::DirectorySeparatorChar
    if (-not $resolved.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase) -or
        (Split-Path $resolved -Leaf) -notlike 'bdo-ci-scope-*') { throw 'Unsafe CI scope fixture cleanup path.' }
    Remove-Item -LiteralPath $resolved -Recurse -Force
}
exit 0
