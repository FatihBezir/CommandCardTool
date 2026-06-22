# Small single-file builds into dist\ (~0.5 MB each). Requires .NET 10 Desktop Runtime on target PC.
$ErrorActionPreference = "Stop"
$root = $PSScriptRoot
$dist = Join-Path $root "dist"
if (Test-Path $dist) { Remove-Item -Recurse -Force $dist }
New-Item -ItemType Directory -Force -Path $dist | Out-Null

$pubArgs = @(
    "-c", "Release",
    "-p:SelfContained=false",
    "-p:PublishSingleFile=true",
    "-p:PublishReadyToRun=false"
)

Write-Host "Building CommandCardTool (single exe, win-x64)..." -ForegroundColor Cyan
dotnet publish "$root\CommandCardTool\CommandCardTool.csproj" @pubArgs -p:RuntimeIdentifier=win-x64
Copy-Item -Force "$root\CommandCardTool\bin\Release\net10.0-windows7.0\win-x64\publish\CommandCardTool.exe" $dist

Write-Host "Building Launcher (single exe, win-x86)..." -ForegroundColor Cyan
dotnet publish "$root\Launcher\Launcher.csproj" @pubArgs -p:Platform=x86 -p:RuntimeIdentifier=win-x86
Copy-Item -Force "$root\Launcher\bin\x86\Release\net10.0-windows7.0\win-x86\publish\Launcher.exe" $dist

Write-Host ""
Write-Host "Ready in dist\ (single exe each):" -ForegroundColor Green
Get-ChildItem $dist -Filter *.exe | ForEach-Object {
    $mb = [math]::Round($_.Length / 1MB, 2)
    Write-Host "  $($_.Name)  ($mb MB)"
}
Write-Host ""
Write-Host "Send only the .exe file(s) from dist\." -ForegroundColor Cyan
Write-Host "Target PC needs .NET 10 Desktop Runtime installed." -ForegroundColor Yellow
Write-Host "For ~150 MB standalone (no runtime), run: .\publish-standalone.ps1" -ForegroundColor DarkGray
