param(
    [ValidateSet('x64', 'x86', 'ARM64')]
    [string]$Platform = 'x64',
    [string]$Configuration = 'Release',
    [string]$Output
)
# Publishes the unpackaged (no MSIX), self-contained build: a folder you can copy anywhere and run
# FoxyBrowser716.exe from, including under Wine/Proton on Linux. See Docs/architecture/unpackaged.md.
$repoRoot = Split-Path $PSScriptRoot -Parent
$project = Join-Path $repoRoot 'FoxyBrowser716/FoxyBrowser716.csproj'
$rid = "win-$($Platform.ToLowerInvariant())"
if (-not $Output) { $Output = Join-Path $repoRoot "publish/unpackaged-$rid" }

$sw = [System.Diagnostics.Stopwatch]::StartNew()
$out = dotnet publish $project -c $Configuration -r $rid -p:Platform=$Platform -p:FoxyUnpackaged=true -o $Output -v:q -nologo 2>&1 | ForEach-Object { "$_" }
$code = $LASTEXITCODE
$sw.Stop()

$diagnostics = $out | Where-Object { $_ -match '(?i)\b(error|warning)\s+[A-Za-z]+\d+' } | Sort-Object -Unique
$diagnostics
if ($code -ne 0 -and -not $diagnostics) { $out | Select-Object -Last 15 }

$elapsed = [Math]::Round($sw.Elapsed.TotalSeconds, 1)
if ($code -eq 0) { Write-Host "OK (${elapsed}s) -> $Output" } else { Write-Host "FAILED exit $code (${elapsed}s)" }
exit $code
