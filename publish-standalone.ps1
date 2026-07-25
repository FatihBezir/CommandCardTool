# Standalone build — no .NET install needed on the target PC.
# EnableCompressionInSingleFile packs the bundled runtime: ~150 MB -> ~65 MB.
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$dist = Join-Path $root "dist-standalone"
New-Item -ItemType Directory -Force -Path $dist | Out-Null

Write-Host "Building CommandCardTool (standalone, no runtime needed)..." -ForegroundColor Cyan
dotnet publish "$root\CommandCardTool\CommandCardTool.csproj" `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true -p:PublishReadyToRun=true -p:EnableCompressionInSingleFile=true
Copy-Item -Force `
  "$root\CommandCardTool\bin\Release\net10.0-windows7.0\win-x64\publish\CommandCardTool.exe" `
  (Join-Path $dist "CommandCardTool.exe")

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
