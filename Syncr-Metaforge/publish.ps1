$ErrorActionPreference = "Stop"

$projectPath = ".\Syncr.UI\Syncr.UI.csproj"
$publishDir = ".\Publish\Windows"
$zipName = "Syncr_Windows_x64.zip"

Write-Host "Cleaning previous build..."
if (Test-Path $publishDir) {
    Remove-Item -Path $publishDir -Recurse -Force -ErrorAction SilentlyContinue
}
if (Test-Path $zipName) {
    Remove-Item -Path $zipName -Force -ErrorAction SilentlyContinue
}

# Deep Clean (v4.22) - Force refresh for Icons and cache
Write-Host "Performing Deep Clean (bin/obj)..."
Get-ChildItem -Path "." -Include bin, obj -Recurse | Remove-Item -Recurse -Force -ErrorAction SilentlyContinue

# Publish as a Single-File Executable (100% safe untrimmed build, no debug symbols)
Write-Host "Publishing Syncr.UI (Windows)..."
dotnet publish $projectPath -c Release -r win-x64 --self-contained -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -o $publishDir

if ($LASTEXITCODE -ne 0) {
    Write-Error "Publish failed!"
}

# Remove PDB symbol files to drastically reduce package size
Get-ChildItem -Path $publishDir -Filter "*.pdb" -Recurse | Remove-Item -Force -ErrorAction SilentlyContinue

<# 🛡️ KURO Shield: Obfuscation temporarily disabled for stability verification
Write-Host "🛡️ KURO Shield: Scrambling Code (Obfuscating)..." -ForegroundColor Cyan
$obfuscar = Get-ChildItem -Path "$HOME\.nuget\packages\obfuscar" -Filter "Obfuscar.Console.exe" -Recurse | Select-Object -First 1
if ($obfuscar) {
    & $obfuscar.FullName ".\obfuscar.xml"
    
    $obfuscatedDir = ".\Obfuscated"
    if (Test-Path $obfuscatedDir) {
        Copy-Item -Path "$obfuscatedDir\*" -Destination $publishDir -Force
        Remove-Item -Path $obfuscatedDir -Recurse -Force
        Write-Host "✅ Obfuscation Complete!" -ForegroundColor Green
    }
}
#>

# Copy shortcut installer if present
if (Test-Path ".\Install_Shortcuts.ps1") {
    Copy-Item ".\Install_Shortcuts.ps1" -Destination $publishDir
}

# Copy Assets folder (v4.10) for external icon references
Copy-Item -Path ".\Syncr.UI\Assets" -Destination $publishDir -Recurse -Force

Write-Host "Creating Zip Archive..."
Compress-Archive -Path "$publishDir\*" -DestinationPath $zipName -Force

Write-Host "Done! Artifacts are in $publishDir and $zipName"
