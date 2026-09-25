$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$generatorPath = Join-Path $scriptDir "Generate-ReleaseNotes.ps1"
$tempRoot = Join-Path ([System.IO.Path]::GetTempPath()) ("bdo-release-notes-tests-" + [guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $tempRoot | Out-Null

$passed = 0
$failed = 0

function Write-JsonFile([string]$Path, $Value) {
    $json = $Value | ConvertTo-Json -Depth 5
    [System.IO.File]::WriteAllText($Path, $json, [System.Text.Encoding]::UTF8)
}

function Invoke-Notes([string]$Path) {
    try {
        $output = & $generatorPath -Version "1.2.2" -Tag "v1.2.2" -Sha ("a" * 40) -AssetName "BDO-UA-Client-v1.2.2-win-x64.zip" -ExeSha256 ("b" * 64) -FragmentsPath $Path
        return @{ Success = $true; Output = ($output -join "`n") }
    } catch {
        return @{ Success = $false; Output = $_.Exception.Message }
    }
}

function Assert-True([string]$Name, [bool]$Condition) {
    if (-not $Condition) { throw "Assertion failed: $Name" }
    Write-Output "PASS: $Name"
}

function Assert-Fails([string]$Name, $Value) {
    Assert-True $Name (-not $Value.Success)
}

try {
    $validPath = Join-Path $tempRoot "valid.json"
    Write-JsonFile $validPath ([ordered]@{
        schema_version = 1
        summary = '  Короткий вступ із `Markdown`.  '
        new = @("перша можливість", "друга можливість із [посиланням](https://example.test)")
        fixed = @("виправлено перевірку")
        reliability = @()
        performance = @("покращено швидкодію")
        changes = @()
        limitations = @()
    })
    $valid = Invoke-Notes $validPath
    Assert-True "valid rendering succeeds" $valid.Success
    Assert-True "exact title and metadata" ($valid.Output.Contains("# Хаб Українізаторів BDO - WWM 1.2.2") -and $valid.Output.Contains("Версія: 1.2.2") -and $valid.Output.Contains("Тег: v1.2.2"))
    $summaryOk = $valid.Output.Contains('Короткий вступ із `Markdown`.')
    $linkOk = $valid.Output.Contains('посилання') -and $valid.Output.Contains('https://example.test')
    Assert-True "summary and markdown preserved" ($summaryOk -and $linkOk)
    $newIndex = $valid.Output.IndexOf("## Що нового")
    $fixedIndex = $valid.Output.IndexOf("## Виправлено")
    $performanceIndex = $valid.Output.IndexOf("## Продуктивність")
    Assert-True "canonical section order" ($newIndex -ge 0 -and $newIndex -lt $fixedIndex -and $fixedIndex -lt $performanceIndex)
    Assert-True "item ordering preserved" ($valid.Output.IndexOf("перша можливість") -lt $valid.Output.IndexOf("друга можливість"))
    Assert-True "asset and hash rendered" ($valid.Output.Contains("BDO-UA-Client-v1.2.2-win-x64.zip") -and $valid.Output.Contains(("b" * 64)))
    Assert-True "Hub heading rendered" ($valid.Output.Contains("# Хаб Українізаторів BDO - WWM 1.2.2") -and -not $valid.Output.Contains("# BDO UA Client 1.2.2"))
    Assert-True "canonical repository links and legacy technical install contract retained" ($valid.Output.Contains("BDO-UA-Client.exe") -and $valid.Output.Contains("https://github.com/merelyigor/ua-localization-hub") -and -not $valid.Output.Contains("https://github.com/merelyigor/bdo-ua-client"))
    Assert-True "Ukrainian installation and links rendered" ($valid.Output.Contains("## Як встановити") -and $valid.Output.Contains("## Посилання") -and $valid.Output.Contains("SmartScreen"))

    $emptyPath = Join-Path $tempRoot "empty.json"
    Write-JsonFile $emptyPath ([ordered]@{ schema_version = 1; summary = ""; new = @(); fixed = @(); reliability = @(); performance = @(); changes = @(); limitations = @() })
    $empty = Invoke-Notes $emptyPath
    Assert-True "empty sections omitted" ($empty.Success -and -not $empty.Output.Contains("## Що нового") -and -not $empty.Output.Contains("## Виправлено"))
    Assert-True "maintenance fallback rendered" $empty.Output.Contains("Технічне обслуговування та внутрішні покращення")

    $publicVersionPath = Join-Path $tempRoot "public-version.json"
    Write-JsonFile $publicVersionPath ([ordered]@{ schema_version = 1; summary = ""; new = @("сумісність із v1.2.2"); fixed = @(); reliability = @(); performance = @(); changes = @(); limitations = @() })
    Assert-True "public version is allowed" (Invoke-Notes $publicVersionPath).Success
    Assert-True "no git-log leakage" (-not $valid.Output.Contains("v15.30") -and -not $valid.Output.Contains("додати фонову перевірку"))

    $invalidCases = @(
        @{ Name = "wrong schema version"; Value = [ordered]@{ schema_version = 2; summary = ""; new = @(); fixed = @(); reliability = @(); performance = @(); changes = @(); limitations = @() } },
        @{ Name = "missing property"; Value = [ordered]@{ schema_version = 1; summary = ""; new = @(); fixed = @(); reliability = @(); performance = @(); changes = @() } },
        @{ Name = "unknown property"; Value = [ordered]@{ schema_version = 1; summary = ""; new = @(); fixed = @(); reliability = @(); performance = @(); changes = @(); limitations = @(); extra = @() } },
        @{ Name = "invalid category type"; Value = [ordered]@{ schema_version = 1; summary = ""; new = "not an array"; fixed = @(); reliability = @(); performance = @(); changes = @(); limitations = @() } },
        @{ Name = "empty array item"; Value = [ordered]@{ schema_version = 1; summary = ""; new = @("  "); fixed = @(); reliability = @(); performance = @(); changes = @(); limitations = @() } }
    )
    foreach ($case in $invalidCases) {
        $path = Join-Path $tempRoot (($case.Name -replace ' ', '-') + ".json")
        Write-JsonFile $path $case.Value
        Assert-Fails $case.Name (Invoke-Notes $path)
    }

    $unsafeTokens = @("R1", "B.2", "PENDING ARCHITECT REVIEW", "v15.30")
    foreach ($token in $unsafeTokens) {
        $path = Join-Path $tempRoot (("unsafe-" + ($token -replace '[^A-Za-z0-9]', '-')) + ".json")
        Write-JsonFile $path ([ordered]@{ schema_version = 1; summary = ""; new = @("текст $token"); fixed = @(); reliability = @(); performance = @(); changes = @(); limitations = @() })
        Assert-Fails "internal token $token" (Invoke-Notes $path)
    }

    $missing = Invoke-Notes (Join-Path $tempRoot "missing.json")
    Assert-Fails "missing source" $missing
    $malformedPath = Join-Path $tempRoot "malformed.json"
    [System.IO.File]::WriteAllText($malformedPath, '{"schema_version": 1,', [System.Text.Encoding]::UTF8)
    Assert-Fails "malformed source" (Invoke-Notes $malformedPath)
    Write-Output ""
    Write-Output "All Generate-ReleaseNotes tests passed."
} catch {
    $failed++
    Write-Error $_
    exit 1
} finally {
    if (Test-Path -LiteralPath $tempRoot) { Remove-Item -LiteralPath $tempRoot -Recurse -Force }
}
