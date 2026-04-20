# Master fix script - Run this to fix all files at once
# Execute from project root: .\run-all-fixes.ps1

Write-Host "🔧 Starting CineLibrary v1.4.0 Fix Script..." -ForegroundColor Green
Write-Host ""

# Run all fix scripts
Write-Host "Step 1/5: Fixing StatisticsPage..." -ForegroundColor Cyan
& .\fix-statistics-page.ps1
Write-Host ""

Write-Host "Step 2/5: Fixing FilterResultsPage..." -ForegroundColor Cyan
& .\fix-filter-results-page.ps1
Write-Host ""

Write-Host "Step 3/5: Fixing LibraryPage..." -ForegroundColor Cyan
& .\fix-library-page.ps1
Write-Host ""

Write-Host "Step 4/5: Fixing KeyboardShortcutsDialog..." -ForegroundColor Cyan
& .\fix-keyboard-shortcuts-dialog.ps1
Write-Host ""

Write-Host "Step 5/5: Checking MainWindow..." -ForegroundColor Cyan
& .\fix-main-window.ps1
Write-Host ""

Write-Host "✅ All fixes completed!" -ForegroundColor Green
Write-Host ""
Write-Host "Next steps:" -ForegroundColor Yellow
Write-Host "1. Open the project in Visual Studio"
Write-Host "2. Run: dotnet build"
Write-Host "3. Check for any remaining errors"
Write-Host "4. Run the app and test:"
Write-Host "   - Click 'Statistics' sidebar button"
Write-Host "   - Press Ctrl+? for keyboard shortcuts"
Write-Host "   - Click actor/director names in movie details"
Write-Host ""
