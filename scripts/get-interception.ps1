# Downloads the x64 interception.dll (and the driver installer) into native\.
# You STILL must install the driver once (admin) and reboot:
#   native\install-interception.exe /install   (then reboot)
$ErrorActionPreference = 'Stop'
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12
$h = @{ 'User-Agent' = 'KeyboardToGamepad-setup' }
if ($env:GITHUB_TOKEN) { $h['Authorization'] = "Bearer $($env:GITHUB_TOKEN)" }   # raise API rate limit in CI
$native = Join-Path $PSScriptRoot '..\native'
New-Item -ItemType Directory -Force -Path $native | Out-Null

Write-Host 'Fetching latest Interception release...'
$rel   = Invoke-RestMethod 'https://api.github.com/repos/oblitum/Interception/releases' -Headers $h
$asset = ($rel | Select-Object -First 1).assets | Where-Object { $_.name -like '*.zip' } | Select-Object -First 1
$zip   = Join-Path $env:TEMP $asset.name
Invoke-WebRequest $asset.browser_download_url -OutFile $zip -Headers $h

$ex = Join-Path $env:TEMP 'interception_dl'
if (Test-Path $ex) { Remove-Item $ex -Recurse -Force }
Expand-Archive $zip $ex -Force

$dll  = Get-ChildItem $ex -Recurse -Filter interception.dll        | Where-Object { $_.FullName -match 'x64' } | Select-Object -First 1
$inst = Get-ChildItem $ex -Recurse -Filter install-interception.exe | Select-Object -First 1
Copy-Item $dll.FullName (Join-Path $native 'interception.dll') -Force
if ($inst) { Copy-Item $inst.FullName (Join-Path $native 'install-interception.exe') -Force }

Write-Host 'Done -> native\interception.dll'
Write-Host 'Next: run (admin)  native\install-interception.exe /install   then REBOOT.'
