$ErrorActionPreference = "Stop"

Write-Host "Building Syncr v2.6.2 for Raspberry Pi (Linux ARM64)..." -ForegroundColor Cyan

# Define paths
$projectPath = Join-Path $PSScriptRoot "Syncr.UI\Syncr.UI.csproj"
$outputDir = Join-Path $PSScriptRoot "Publish\Pi"
$zipPath = Join-Path $PSScriptRoot "publish-pi-v26.zip"

Write-Host "Cleaning previous build..."
if (Test-Path $outputDir) {
    Remove-Item $outputDir -Recurse -Force
}
if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

# Ultra-Deep Clean (v2.6) - Ensuring no stale XAML remains
Write-Host "RESCUE: Performing Ultra-Deep Clean (dotnet clean + manual wipe)..."
dotnet clean $projectPath -c Release
Get-ChildItem -Path "." -Include bin, obj -Recurse | ForEach-Object { 
    Remove-Item -Path $_.FullName -Recurse -Force -ErrorAction SilentlyContinue 
}

# Run dotnet publish
Write-Host "Publishing Syncr.UI v2.6.2 (Linux ARM64)..."
dotnet publish $projectPath -c Release -r linux-arm64 --self-contained -o $outputDir

if ($LASTEXITCODE -eq 0) {
    # KURO Shield: Run Obfuscation
    Write-Host "KURO Shield: Obfuscating Build..." -ForegroundColor Cyan
    $obfuscar = Get-ChildItem -Path "$HOME\.nuget\packages\obfuscar" -Filter "Obfuscar.Console.exe" -Recurse | Select-Object -First 1
    if ($obfuscar) {
        & $obfuscar.FullName (Join-Path $PSScriptRoot "obfuscar_pi.xml")
        
        $obfuscatedDir = Join-Path $PSScriptRoot "Obfuscated_Pi"
        if (Test-Path $obfuscatedDir) {
            Copy-Item -Path "$obfuscatedDir\*" -Destination $outputDir -Force
            Remove-Item -Path $obfuscatedDir -Recurse -Force
            Write-Host "Obfuscar Success!" -ForegroundColor Green
        }
    }

    # Copy assets
    Write-Host "Copying Assets (v2.6.2)..." -ForegroundColor Cyan
    Copy-Item -Path (Join-Path $PSScriptRoot "Syncr.UI\Assets") -Destination $outputDir -Recurse -Force

    # Include updated Modbus Slave Simulation script
    Write-Host "Including Modbus Slave Simulator..." -ForegroundColor Cyan
    Copy-Item -Path (Join-Path $PSScriptRoot "modbus_slave.py") -Destination $outputDir -Force

    # Include Master Changelog in the zip
    Copy-Item -Path (Join-Path $PSScriptRoot "SYNCR_MASTER_CHANGELOG.txt") -Destination $outputDir -Force

    Write-Host "Creating Zip Archive: $zipPath..." -ForegroundColor Cyan
    Compress-Archive -Path "$outputDir\*" -DestinationPath $zipPath -Force
    
    Write-Host "Zip ready at: $zipPath" -ForegroundColor Green
}
else {
    Write-Host "Build Failed." -ForegroundColor Red
    exit 1
}
