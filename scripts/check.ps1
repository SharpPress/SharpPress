param(
    [ValidateSet('fast', 'task', 'full')]
    [string]$Stage = 'task',
    [string]$Base = 'HEAD',
    [switch]$RequireSecretScan
)

$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')

$baseline = Get-Content constraints.baseline.json -Raw | ConvertFrom-Json
$enforce = (Get-Date) -ge [datetime]$baseline.warnUntil
$script:blocked = $false

function Write-Block([string]$message) {
    Write-Host "FAIL  $message" -ForegroundColor Red
    $script:blocked = $true
}

function Write-Soft([string]$message) {
    if ($enforce) { Write-Block $message }
    else { Write-Host "WARN  $message" -ForegroundColor Yellow }
}

function Write-Ok([string]$message) {
    Write-Host "OK    $message" -ForegroundColor Green
}

git rev-parse --verify --quiet "$Base^{commit}" | Out-Null
if ($LASTEXITCODE -ne 0) { throw "Unknown base revision '$Base'" }

$added = New-Object System.Collections.Generic.List[object]
$file = $null
$line = 0
foreach ($row in (git diff -U0 --no-color --no-ext-diff $Base -- .)) {
    if ($row.StartsWith('+++ ')) {
        $file = if ($row.StartsWith('+++ b/')) { $row.Substring(6) } else { $null }
    }
    elseif ($row -match '^@@ -\S+ \+(\d+)') {
        $line = [int]$Matches[1]
    }
    elseif ($file -and $row.StartsWith('+')) {
        $added.Add([pscustomobject]@{ File = $file; Line = $line; Text = $row.Substring(1) })
        $line++
    }
}
foreach ($untracked in (git ls-files --others --exclude-standard)) {
    $number = 1
    foreach ($text in @(Get-Content -LiteralPath $untracked)) {
        $added.Add([pscustomobject]@{ File = $untracked; Line = $number; Text = $text })
        $number++
    }
}

$forbidden = '#pragma\s+warning\s+disable|SuppressMessage|<NoWarn>|NotImplementedException|\[(Fact|Theory)\([^\]]*Skip\s*=|catch\s*(\([^)]*\))?\s*\{\s*\}|nosemgrep|gitleaks:allow|ExcludeFromCodeCoverage'
$hits = @($added | Where-Object { $_.File -match '\.(cs|cshtml|csproj|props)$' -and $_.Text -match $forbidden })
foreach ($hit in $hits) { Write-Block "floor: $($hit.File):$($hit.Line) $($hit.Text.Trim())" }

$deletedTests = @(git diff --name-only --diff-filter=D $Base -- Tests)
foreach ($deleted in $deletedTests) { Write-Block "floor: test file deleted: $deleted" }
if ($hits.Count -eq 0 -and $deletedTests.Count -eq 0) { Write-Ok 'floor: no new suppressions, stubs, skipped or deleted tests' }

if (git diff --name-only $Base -- CONSTRAINTS.md constraints.baseline.json scripts/check.ps1) {
    Write-Host 'NOTE  the quality bar itself changed in this diff; review it' -ForegroundColor Magenta
}

if (Get-Command gitleaks -ErrorAction SilentlyContinue) {
    ($added | ForEach-Object { $_.Text }) -join "`n" | gitleaks stdin --redact --no-banner
    if ($LASTEXITCODE -ne 0) { Write-Block 'floor: gitleaks reported a finding or failed' }
    else { Write-Ok 'floor: no secrets in added lines' }
}
elseif ($RequireSecretScan) {
    Write-Block 'floor: gitleaks is not installed'
}
else {
    Write-Host 'SKIP  secrets: gitleaks is not installed locally (CI runs it)' -ForegroundColor Yellow
}

$build = dotnet build SharpPress.sln --nologo --no-incremental
if ($LASTEXITCODE -ne 0) {
    $build | Where-Object { $_ -match ': error ' } | Select-Object -Unique | ForEach-Object { Write-Host $_ }
    Write-Block 'floor: build failed'
}
else {
    Write-Ok 'floor: build has 0 errors'
    $summary = $build | Select-String '^\s*(\d+) Warning\(s\)' | Select-Object -Last 1
    if (-not $summary) { Write-Block 'build warning count not found' }
    else {
        $warnings = [int]$summary.Matches[0].Groups[1].Value
        if ($warnings -gt $baseline.buildWarnings) { Write-Soft "build warnings rose to $warnings (baseline $($baseline.buildWarnings))" }
        else { Write-Ok "build warnings $warnings (baseline $($baseline.buildWarnings))" }
    }
}

if ($Stage -ne 'fast' -and -not $script:blocked) {
    $results = Join-Path ([IO.Path]::GetTempPath()) ('sharppress-coverage-' + [guid]::NewGuid())
    dotnet test Tests/SharpPress.Tests/SharpPress.Tests.csproj --nologo --collect 'XPlat Code Coverage' --results-directory $results
    if ($LASTEXITCODE -ne 0) { Write-Soft 'tests failed (unit or architecture)' }
    else { Write-Ok 'unit and architecture tests pass' }

    $report = Get-ChildItem $results -Recurse -Filter coverage.cobertura.xml -ErrorAction SilentlyContinue | Select-Object -First 1
    if (-not $report) { Write-Soft 'no coverage report was produced' }
    else {
        [xml]$xml = Get-Content $report.FullName -Raw
        $root = (Get-Location).Path.Replace('\', '/').TrimEnd('/') + '/'
        $sources = @($xml.coverage.sources.source)
        $covered = @{}
        foreach ($class in $xml.SelectNodes('//class')) {
            $path = $class.filename.Replace('\', '/')
            if (-not [IO.Path]::IsPathRooted($path)) {
                foreach ($source in $sources) {
                    $candidate = $source.Replace('\', '/').TrimEnd('/') + '/' + $path
                    if (Test-Path -LiteralPath $candidate) { $path = $candidate; break }
                }
            }
            if ($path.StartsWith($root, [StringComparison]::OrdinalIgnoreCase)) { $path = $path.Substring($root.Length) }
            foreach ($entry in $class.SelectNodes('lines/line')) {
                $key = "$path`:$($entry.number)"
                $covered[$key] = ($covered[$key] -or [int]$entry.hits -gt 0)
            }
        }

        $measured = @($added | Where-Object { $_.File -match '^(Server|Abstractions)/.*\.cs$' -and $covered.ContainsKey("$($_.File):$($_.Line)") })
        if ($measured.Count -eq 0) { Write-Ok 'changed-line coverage: no changed executable lines' }
        else {
            $hit = @($measured | Where-Object { $covered["$($_.File):$($_.Line)"] }).Count
            $percent = [math]::Round(100 * $hit / $measured.Count, 1)
            if ($percent -lt $baseline.changedLineCoverage) { Write-Soft "changed-line coverage $percent% (needs $($baseline.changedLineCoverage)%)" }
            else { Write-Ok "changed-line coverage $percent%" }
        }

        $project = [math]::Round(100 * [double]::Parse($xml.coverage.'line-rate', [Globalization.CultureInfo]::InvariantCulture), 2)
        if ($project -lt $baseline.projectLineCoverage - $baseline.coverageTolerance) { Write-Soft "project coverage fell to $project% (baseline $($baseline.projectLineCoverage)%)" }
        else { Write-Ok "project coverage $project% (baseline $($baseline.projectLineCoverage)%)" }
    }
    Remove-Item -LiteralPath $results -Recurse -Force -ErrorAction SilentlyContinue
}

if ($Stage -eq 'full') {
    $json = dotnet list SharpPress.sln package --vulnerable --include-transitive --format json
    if ($LASTEXITCODE -ne 0) { Write-Block 'dependency scan failed to run' }
    else {
        $scan = ($json -join "`n") | ConvertFrom-Json
        if (-not $scan.projects) { Write-Block 'dependency scan returned no projects' }
        $new = New-Object System.Collections.Generic.List[string]
        foreach ($project in $scan.projects) {
            foreach ($framework in $project.frameworks) {
                foreach ($package in (@($framework.topLevelPackages) + @($framework.transitivePackages))) {
                    foreach ($vulnerability in $package.vulnerabilities) {
                        $id = $vulnerability.advisoryurl.Split('/')[-1]
                        if ($vulnerability.severity -in 'High', 'Critical' -and $baseline.knownAdvisories -notcontains $id) {
                            $new.Add("$($package.id) $($package.resolvedVersion) $($vulnerability.severity) $id")
                        }
                    }
                }
            }
        }
        $unique = @($new | Sort-Object -Unique)
        foreach ($item in $unique) { Write-Soft "new vulnerable dependency: $item" }
        if ($unique.Count -eq 0) { Write-Ok 'no new High or Critical dependency advisories' }
    }
}

if ($script:blocked) { exit 1 }
exit 0
