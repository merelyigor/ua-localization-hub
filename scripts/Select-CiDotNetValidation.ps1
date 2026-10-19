param(
    [string]$EventName = $env:GITHUB_EVENT_NAME,
    [string]$BaseSha = $env:GITHUB_BASE_SHA,
    [string]$HeadSha = $env:GITHUB_SHA,
    [string[]]$ChangedPaths = @(),
    [switch]$UseProvidedPaths,
    [string]$OutputPath = $env:GITHUB_OUTPUT
)

$ErrorActionPreference = 'Stop'
$runDotNet = $true
$reason = 'full validation is the safe default'

if ($UseProvidedPaths) {
    $paths = @($ChangedPaths)
} elseif ($EventName -eq 'workflow_dispatch' -or
    [string]::IsNullOrWhiteSpace($BaseSha) -or [string]::IsNullOrWhiteSpace($HeadSha) -or
    $BaseSha -match '^0+$') {
    $paths = @()
    $reason = 'manual, initial, or unbound run requires full validation'
} else {
    $diffOutput = @(& git -c core.quotepath=false diff --no-ext-diff --no-renames --name-only $BaseSha $HeadSha 2>&1)
    if ($LASTEXITCODE -ne 0) {
        $paths = @()
        $reason = 'Git could not determine the change set; full validation required'
    } else {
        $paths = @($diffOutput)
    }
}

if ($paths.Count -gt 0 -and $EventName -ne 'workflow_dispatch') {
    $dotNetInputsChanged = @($paths | Where-Object {
        $path = $_ -replace '\\', '/'
        $isDocumentationPath = $path.StartsWith('docs/', [StringComparison]::Ordinal)
        $isDocumentationFile = $isDocumentationPath -and [IO.Path]::GetExtension($path) -cin @('.md', '.json', '.png')
        $isAgentOnlyFile = $path -cin @('AGENTS.md', 'README.md', 'LICENSE', 'LICENSE.md', '.codex/config.toml')
        -not ($isDocumentationFile -or $isAgentOnlyFile)
    })
    if ($dotNetInputsChanged.Count -eq 0) {
        $runDotNet = $false
        $reason = 'all changed paths are documentation or agent-only configuration'
    } else {
        $reason = 'at least one application, test, workflow, script, or unknown path changed'
    }
}

$value = if ($runDotNet) { 'true' } else { 'false' }
if (-not [string]::IsNullOrWhiteSpace($OutputPath)) {
    Add-Content -LiteralPath $OutputPath -Value "run_dotnet=$value" -Encoding utf8
}
Write-Output "CI .NET validation: $value ($reason)"
exit 0
