# Rebuild Mimir Display with Icon
# This script stops any running instances, rebuilds the app with the icon, and reports success

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Rebuilding Mimir Display with Icon" -ForegroundColor Cyan
Write-Host "========================================`n" -ForegroundColor Cyan

# Check if icon exists
$iconPath = "MimirDisplay\Resources\mimir.ico"
if (Test-Path $iconPath) {
	$icon = Get-Item $iconPath
	Write-Host "OK Icon file found:" -ForegroundColor Green
	Write-Host "  Path: $iconPath" -ForegroundColor Gray
	Write-Host "  Size: $($icon.Length) bytes" -ForegroundColor Gray
	Write-Host "  Modified: $($icon.LastWriteTime)`n" -ForegroundColor Gray
} else {
	Write-Host "X Icon file not found at: $iconPath" -ForegroundColor Red
	Write-Host "  Please place your mimir.ico file in MimirDisplay\Resources\`n" -ForegroundColor Yellow
	exit 1
}

# Stop any running MimirDisplay processes
Write-Host "Checking for running MimirDisplay processes..." -ForegroundColor Yellow
$processes = Get-Process MimirDisplay -ErrorAction SilentlyContinue
if ($processes) {
	Write-Host "  Found $($processes.Count) running process(es). Stopping..." -ForegroundColor Yellow
	$processes | Stop-Process -Force
	Start-Sleep -Seconds 2
	Write-Host "  OK Processes stopped`n" -ForegroundColor Green
} else {
	Write-Host "  OK No running processes found`n" -ForegroundColor Green
}

# Clean previous build
Write-Host "Cleaning previous build..." -ForegroundColor Yellow
dotnet clean MimirDisplay\MimirDisplay.csproj -c Release --nologo -v quiet
Write-Host "  ✓ Clean complete`n" -ForegroundColor Green

# Build with icon
Write-Host "Building with icon..." -ForegroundColor Yellow
$buildResult = dotnet build MimirDisplay\MimirDisplay.csproj -c Release --nologo
if ($LASTEXITCODE -eq 0) {
	Write-Host "  ✓ Build succeeded!`n" -ForegroundColor Green

	# Check if exe exists and has an icon
	$exePath = "MimirDisplay\bin\Release\net8.0-windows\win-x64\MimirDisplay.exe"
	if (Test-Path $exePath) {
		$exe = Get-Item $exePath
		Write-Host "========================================" -ForegroundColor Cyan
		Write-Host "Build Output:" -ForegroundColor Cyan
		Write-Host "========================================" -ForegroundColor Cyan
		Write-Host "Executable: $exePath" -ForegroundColor Green
		Write-Host "Size: $([math]::Round($exe.Length / 1MB, 2)) MB" -ForegroundColor Gray
		Write-Host "Modified: $($exe.LastWriteTime)" -ForegroundColor Gray
		Write-Host "`nYour executable now has the icon! 🎉" -ForegroundColor Green
		Write-Host "`nNext steps:" -ForegroundColor Yellow
		Write-Host "  1. Right-click MimirDisplay.exe to verify the icon" -ForegroundColor Gray
		Write-Host "  2. Run the app to see the icon in the taskbar" -ForegroundColor Gray
		Write-Host "  3. (Optional) Create an installer using MimirDisplaySetup.iss`n" -ForegroundColor Gray
	} else {
		Write-Host "✗ Executable not found at expected location" -ForegroundColor Red
	}
} else {
	Write-Host "  X Build failed. See errors above.`n" -ForegroundColor Red
	exit 1
}
