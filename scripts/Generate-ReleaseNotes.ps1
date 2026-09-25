param(
    [Parameter(Mandatory = $true)] [string]$Version,
    [Parameter(Mandatory = $true)] [string]$Tag,
    [Parameter(Mandatory = $true)] [string]$Sha,
    [Parameter(Mandatory = $true)] [string]$AssetName,
    [Parameter(Mandatory = $false)] [string]$ExeSha256,
    [Parameter(Mandatory = $false)] [string]$FragmentsPath = "docs/releases/NEXT.json"
)

$ErrorActionPreference = "Stop"

function Assert-NonEmpty([string]$Name, [string]$Value) {
    if ([string]::IsNullOrWhiteSpace($Value)) { throw "$Name must not be empty." }
}

Assert-NonEmpty "Version" $Version
Assert-NonEmpty "Tag" $Tag
Assert-NonEmpty "Sha" $Sha
Assert-NonEmpty "AssetName" $AssetName
Assert-NonEmpty "ExeSha256" $ExeSha256
Assert-NonEmpty "FragmentsPath" $FragmentsPath

$fragmentFile = Resolve-Path -LiteralPath $FragmentsPath -ErrorAction Stop
$jsonText = [System.IO.File]::ReadAllText($fragmentFile.Path, [System.Text.Encoding]::UTF8)
try { $source = $jsonText | ConvertFrom-Json }
catch { throw "Release-note source JSON is malformed: $($_.Exception.Message)" }
if ($null -eq $source) { throw "Release-note source must contain a JSON object." }

$requiredProperties = @("schema_version", "summary", "new", "fixed", "reliability", "performance", "changes", "limitations")
$actualProperties = @($source.PSObject.Properties.Name)
foreach ($required in $requiredProperties) {
    if ($actualProperties -notcontains $required) { throw "Release-note source is missing required property '$required'." }
}
foreach ($actual in $actualProperties) {
    if ($requiredProperties -notcontains $actual) { throw "Release-note source contains unknown top-level property '$actual'." }
}
$integerSchemaType = $source.schema_version -is [byte] -or $source.schema_version -is [int16] -or $source.schema_version -is [int32] -or $source.schema_version -is [int64]
if (-not $integerSchemaType -or $source.schema_version -ne 1) { throw "Release-note source schema_version must be integer 1." }
if ($source.summary -isnot [string]) { throw "Release-note source summary must be a string." }

$categoryNames = @("new", "fixed", "reliability", "performance", "changes", "limitations")
$categories = @{}
foreach ($categoryName in $categoryNames) {
    $category = $source.PSObject.Properties[$categoryName].Value
    if ($category -isnot [System.Array]) { throw "Release-note source category '$categoryName' must be an array." }
    $items = @()
    foreach ($item in @($category)) {
        if ($item -isnot [string]) { throw "Release-note source category '$categoryName' must contain only strings." }
        $trimmed = $item.Trim()
        if ([string]::IsNullOrWhiteSpace($trimmed)) { throw "Release-note source category '$categoryName' contains an empty item." }
        $items += $trimmed
    }
    $categories[$categoryName] = $items
}

$publicText = @($source.summary.Trim()) + @($categoryNames | ForEach-Object { $categories[$_] })
$unsafePatterns = @(
    "PENDING ARCHITECT REVIEW",
    "REVIEWED / ACCEPTED",
    "\bPlan ID\b",
    "\bPRIMARY\b",
    "\bStage [A-E]\b",
    "(?<![A-Za-z0-9])R[1-3](?![A-Za-z0-9])",
    "\bB\.[1-3]\b",
    "\bv15\.\d+\b"
)
foreach ($text in $publicText) {
    foreach ($pattern in $unsafePatterns) {
        if ($text -match $pattern) { throw "Unsafe internal release-note content detected." }
    }
}

$sectionMap = [ordered]@{
    "new" = "## Що нового"
    "fixed" = "## Виправлено"
    "reliability" = "## Надійність"
    "performance" = "## Продуктивність"
    "changes" = "## Зміни"
    "limitations" = "## Відомі проблеми / обмеження"
}
$sections = @()
if (-not [string]::IsNullOrWhiteSpace($source.summary.Trim())) { $sections += $source.summary.Trim() }
foreach ($categoryName in $sectionMap.Keys) {
    $items = $categories[$categoryName]
    if ($items.Count -gt 0) {
        $renderedItems = (@($items | ForEach-Object { "- $_" }) -join "`n")
        $sections += "$($sectionMap[$categoryName])`n`n$renderedItems"
    }
}
if ($sections.Count -eq 0) { $sections += "## Зміни`n`n- Технічне обслуговування та внутрішні покращення без окремих користувацьких нововведень." }

$body = $sections -join "`n`n"
$notes = @"
# Хаб Українізаторів BDO - WWM $Version

Версія: $Version
Тег: $Tag
Commit: $Sha

$body

## Завантаження

``$AssetName``

Internal EXE SHA-256:

``$ExeSha256``

## Як встановити

1. Завантажте ``$AssetName`` зі сторінки цього релізу
2. Розпакуйте архів
3. Запустіть ``BDO-UA-Client.exe``
4. Якщо Windows SmartScreen покаже попередження — натисніть "Докладніше" → "Виконати" (деталі: [README](https://github.com/merelyigor/ua-localization-hub#windows-smartscreen))

## Посилання

- [bdo-ua.com.ua](https://bdo-ua.com.ua/)
- [Репозиторій](https://github.com/merelyigor/ua-localization-hub)
- [Інструкція](https://github.com/merelyigor/ua-localization-hub#readme)
- [Технічна документація](https://github.com/merelyigor/ua-localization-hub/blob/main/docs/index.md)
"@

return $notes.TrimEnd()
