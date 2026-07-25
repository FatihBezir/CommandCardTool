# Single-exe builds into dist\.
#   CommandCardTool -> net48, runs on any Windows 10 1903+ with NO runtime install.
#   Launcher        -> net10, needs the .NET 10 Desktop Runtime (it uses modern socket APIs).
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$dist = Join-Path $root "dist"
New-Item -ItemType Directory -Force -Path $dist | Out-Null

Write-Host "Building CommandCardTool (net48, no runtime needed)..." -ForegroundColor Cyan
dotnet publish "$root\CommandCardTool\CommandCardTool.csproj" -c Release
Copy-Item -Force "$root\CommandCardTool\bin\Release\net48\publish\CommandCardTool.exe" $dist

Write-Host "Building Launcher (net10, single exe, win-x86)..." -ForegroundColor Cyan
dotnet publish "$root\Launcher\Launcher.csproj" `
  -c Release -p:SelfContained=false -p:PublishSingleFile=true -p:PublishReadyToRun=false `
  -p:Platform=x86 -p:RuntimeIdentifier=win-x86
Copy-Item -Force "$root\Launcher\bin\x86\Release\net10.0-windows7.0\win-x86\publish\Launcher.exe" $dist

Write-Host ""
Write-Host "Ready in dist\:" -ForegroundColor Green
Get-ChildItem $dist -Filter *.exe | ForEach-Object {
    $mb = [math]::Round($_.Length / 1MB, 2)
    Write-Host "  $($_.Name)  ($mb MB)"
}
Write-Host ""
Write-Host "CommandCardTool.exe  - copy it anywhere, no install needed." -ForegroundColor Cyan
Write-Host "Launcher.exe         - target PC needs .NET 10 Desktop Runtime." -ForegroundColor Yellow
Write-Host "Runtime-free Launcher build: .\publish-standalone.ps1" -ForegroundColor DarkGray
