# Downloads the latest ViGEmBus installer into native\ so the build can embed it in the exe.
# (DriverSetup extracts and runs it on a PC where ViGEmBus is missing.)
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$h = @{ 'User-Agent' = 'KeyboardToGamepad-setup' }
if ($env:GITHUB_TOKEN) { $h['Authorization'] = "Bearer $($env:GITHUB_TOKEN)" }   # raise API rate limit in CI
$native = Join-Path $PSScriptRoot '..\native'
New-Item -ItemType Directory -Force -Path $native | Out-Null

Write-Host 'Fetching latest ViGEmBus release...'
$rel   = Invoke-RestMethod 'https://api.github.com/repos/nefarius/ViGEmBus/releases/latest' -Headers $h
# The installer asset is the x64/x86/arm64 .exe (e.g. ViGEmBus_1.22.0_x64_x86_arm64.exe).
$asset = $rel.assets | Where-Object { $_.name -like '*_x64_*.exe' } | Select-Object -First 1
if (-not $asset) { $asset = $rel.assets | Where-Object { $_.name -like '*.exe' } | Select-Object -First 1 }
if (-not $asset) { throw 'No ViGEmBus .exe installer asset found in the latest release.' }

# Stable local name so the .csproj can reference it without tracking the version.
$dest = Join-Path $native 'ViGEmBus_x64_x86_arm64.exe'
Write-Host "Downloading $($asset.name)..."
Invoke-WebRequest $asset.browser_download_url -OutFile $dest -Headers $h

Write-Host "Done -> native\ViGEmBus_x64_x86_arm64.exe"
