# Standalone Launcher build — bundles the .NET 10 runtime, no install needed.
# (CommandCardTool targets net48 and already needs no runtime: use publish.ps1)
# EnableCompressionInSingleFile packs the bundled runtime: ~150 MB -> ~65 MB.
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$dist = Join-Path $root "dist-standalone"
New-Item -ItemType Directory -Force -Path $dist | Out-Null

Write-Host "Building Launcher (standalone, no runtime needed)..." -ForegroundColor Cyan
dotnet publish "$root\Launcher\Launcher.csproj" `
  -c Release -r win-x86 --self-contained true `
  -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:EnableCompressionInSingleFile=true
Copy-Item -Force `
  "$root\Launcher\bin\Release\net10.0-windows7.0\win-x86\publish\Launcher.exe" `
  (Join-Path $dist "Launcher.exe")

Write-Host ""
Write-Host "Standalone builds (no runtime install):" -ForegroundColor Green
Get-ChildItem $dist -Filter *.exe | ForEach-Object {
    $mb = [math]::Round($_.Length / 1MB, 1)
    Write-Host "  $($_.FullName)  ($mb MB)"
}
