# Publish Mimir Display as Single-File Executable
# This script builds a production-ready single-file .exe

param(
	[string]$Configuration = "Release",
	[string]$OutputDir = "publish",
	[switch]$SkipClean
)

Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Mimir Display - Single-File Publisher" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""

# Clean output directory
if (-not $SkipClean) {
	if (Test-Path $OutputDir) {
		Write-Host "Cleaning output directory: $OutputDir" -ForegroundColor Yellow
		Remove-Item -Path $OutputDir -Recurse -Force
	}
}

# Create output directory
New-Item -ItemType Directory -Path $OutputDir -Force | Out-Null

Write-Host "Publishing configuration: $Configuration" -ForegroundColor Green
Write-Host "Output directory: $OutputDir" -ForegroundColor Green
Write-Host ""

# Publish single-file executable
Write-Host "Building single-file executable..." -ForegroundColor Cyan

$publishArgs = @(
	"publish",
	"MimirDisplay\MimirDisplay.csproj",
	"-c", $Configuration,
	"-r", "win-x64",
	"--self-contained", "true",
	"-p:PublishSingleFile=true",
	"-p:IncludeNativeLibrariesForSelfExtract=true",
	"-p:EnableCompressionInSingleFile=true",
	"-o", $OutputDir,
	"--nologo",
	"-v", "minimal"
)

$result = & dotnet @publishArgs

if ($LASTEXITCODE -ne 0) {
	Write-Host "❌ Publish failed!" -ForegroundColor Red
	exit 1
}

Write-Host ""
Write-Host "✅ Publish successful!" -ForegroundColor Green
Write-Host ""

# Show output files
Write-Host "Output files:" -ForegroundColor Cyan
Get-ChildItem $OutputDir -File | ForEach-Object {
	$sizeMB = [math]::Round($_.Length / 1MB, 2)
	Write-Host "  $($_.Name) - $sizeMB MB" -ForegroundColor White
}

Write-Host ""
Write-Host "Main executable: $OutputDir\MimirDisplay.exe" -ForegroundColor Green

# Check for .env file
$envSource = "MimirDisplay\.env"
$envDest = "$OutputDir\.env"

if (Test-Path $envSource) {
	Write-Host ""
	Write-Host "Copying .env file..." -ForegroundColor Yellow
	Copy-Item $envSource $envDest -Force
	Write-Host "✅ .env copied to $OutputDir" -ForegroundColor Green
} else {
	Write-Host ""
	Write-Host "⚠️  No .env file found. You may need to create one in the output directory." -ForegroundColor Yellow
}

Write-Host ""
Write-Host "========================================" -ForegroundColor Cyan
Write-Host "Done! Ready to distribute." -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Cyan
Write-Host ""
Write-Host "To run: .\$OutputDir\MimirDisplay.exe" -ForegroundColor White
