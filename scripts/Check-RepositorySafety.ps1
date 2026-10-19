param(
    [string]$RepositoryRoot = (Split-Path -Parent $PSScriptRoot),
    [string[]]$Paths = @()
)

$ErrorActionPreference = 'Stop'
$root = (Resolve-Path -LiteralPath $RepositoryRoot).Path
Push-Location $root
try {
    if ($Paths.Count -eq 0) {
        $Paths = @(git -c core.quotepath=false ls-files)
        if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate tracked files for safety scan.' }
        $untracked = @(git -c core.quotepath=false ls-files --others --exclude-standard)
        if ($LASTEXITCODE -ne 0) { throw 'Cannot enumerate new source files for safety scan.' }
        # Старі untracked review ZIP/patch/status artifacts не є source. Після stage
        # усі файли (включно із забороненими artifacts) перевіряються без цього filter.
        $Paths += @($untracked | Where-Object { [IO.Path]::GetExtension($_) -in
            @('.md', '.cs', '.json', '.ps1', '.yml', '.yaml', '.toml', '.xml', '.csproj', '.props', '.targets') })
    }
    $patterns = @(
        '-----BEGIN (?:RSA |EC |OPENSSH |DSA )?PRIVATE KEY-----',
        '\bgh[pousr]_[A-Za-z0-9]{30,}\b',
        '\bgithub_pat_[A-Za-z0-9_]{40,}\b',
        '\bAKIA[0-9A-Z]{16}\b',
        '\bsk-(?:proj-)?[A-Za-z0-9_-]{40,}\b',
        '(?i)(?:password|api[_-]?key|client[_-]?secret|access[_-]?token)\s*[:=]\s*["''][^"''\s]{16,}["'']'
    )
    $checked = 0
    foreach ($relative in ($Paths | Sort-Object -Unique)) {
        $full = [IO.Path]::GetFullPath((Join-Path $root $relative))
        $prefix = [IO.Path]::TrimEndingDirectorySeparator($root) + [IO.Path]::DirectorySeparatorChar
        if (-not $full.StartsWith($prefix, [StringComparison]::OrdinalIgnoreCase)) {
            throw 'Safety scan target escapes repository.'
        }
        $cursor = $full
        while (-not $cursor.Equals($root, [StringComparison]::OrdinalIgnoreCase)) {
            try {
                if (([IO.File]::GetAttributes($cursor) -band [IO.FileAttributes]::ReparsePoint) -ne 0) {
                    throw "Reparse point in safety scan target: $relative"
                }
            } catch [IO.FileNotFoundException] {
                # Видалений tracked файл не читаємо; перевірка батьків триває.
            } catch [IO.DirectoryNotFoundException] {
                # Відсутній нащадок не скасовує перевірку наявних батьків.
            }
            $cursor = [IO.Path]::GetDirectoryName($cursor)
        }
        if ($relative -match '(?i)(^|[/\\])(?:\.env(?:\..*)?|config\.json|appsettings\.Local\.json)$' -or
            $relative -match '(?i)\.(?:pem|key|pfx|p12|log|patch|exe|dll|zip|7z)$' -or
            $relative -match '(?i)(^|[/\\])(?:bin|obj|artifacts|TestResults)[/\\]' -or
            $relative -match '(?i)^\.agents[/\\]skills[/\\]') {
            throw "Private/generated/review artifact must not be tracked: $relative"
        }
        if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { continue }
        if ([IO.Path]::GetExtension($full) -notin @('.md', '.cs', '.json', '.ps1', '.yml', '.yaml', '.toml', '.xml', '.csproj', '.props', '.targets', '.txt')) { continue }
        $body = [IO.File]::ReadAllText($full)
        foreach ($pattern in $patterns) {
            if ([regex]::IsMatch($body, $pattern)) {
                # Не друкувати matched bytes: вони можуть бути справжнім секретом.
                throw "Potential secret in $relative; inspect locally before commit."
            }
        }
        $checked++
    }
    Write-Output "Repository safety scan passed: $checked text files. Manual private-data diff review is still required."
} finally { Pop-Location }
