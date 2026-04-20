# Build and test script after fixes
# Execute from project root: .\build-and-test.ps1

Write-Host "🚀 Building CineLibrary v1.4.0..." -ForegroundColor Green
Write-Host ""

# Clean and build
Write-Host "Step 1: Cleaning previous build..." -ForegroundColor Cyan
dotnet clean | Out-Null
Write-Host "✅ Cleaned"
Write-Host ""

Write-Host "Step 2: Building project..." -ForegroundColor Cyan
$buildOutput = dotnet build 2>&1
$buildOutput | ForEach-Object { Write-Host $_ }

# Check for errors
if ($LASTEXITCODE -eq 0)
{
    Write-Host ""
    Write-Host "✅ BUILD SUCCESSFUL!" -ForegroundColor Green
    Write-Host ""
    Write-Host "🧪 Ready to test:" -ForegroundColor Yellow
    Write-Host "1. Run: dotnet run"
    Write-Host "2. Test Statistics page (📊 button in sidebar)"
    Write-Host "3. Test Keyboard shortcuts (? button in titlebar)"
    Write-Host "4. Test Actor/Director filters (click names in movie detail)"
    Write-Host ""
}
else
{
    Write-Host ""
    Write-Host "❌ BUILD FAILED" -ForegroundColor Red
    Write-Host "Please review errors above and fix them"
    Write-Host ""
}
