# Fix MainWindow.xaml.cs - Add missing methods
# Run from project root

$file = "MainWindow.xaml.cs"
$content = Get-Content -Path $file -Raw

# Find the line with "NavigateToAllMovies" method and replace the section
$methodsToAdd = @"

    public void RefreshSidebar()
    {
        // Refresh sidebar data
        _ = RefreshSidebarAsync();
    }
"@

# Check if RefreshSidebar method already exists
if ($content -notlike "*public void RefreshSidebar()*")
{
    # Find a good place to add it - look for the last navigation handler
    $insertPoint = $content.LastIndexOf("private void OnNavStatistics")
    if ($insertPoint -gt 0)
    {
        # Find the end of OnHelpClick method
        $searchFrom = $insertPoint
        $endOfMethod = $content.IndexOf("`n    }", $searchFrom)
        if ($endOfMethod -gt 0)
        {
            $insertAt = $endOfMethod + 5
            $newContent = $content.Insert($insertAt, $methodsToAdd)
            Set-Content -Path $file -Value $newContent -Encoding UTF8
            Write-Host "✅ Added RefreshSidebar() method"
        }
    }
}
else
{
    Write-Host "✅ RefreshSidebar() method already exists"
}

# Also verify NavigateToFilterResults and NavigateToAllMovies exist
if ($content -notlike "*public void NavigateToFilterResults*")
{
    Write-Host "⚠️  WARNING: NavigateToFilterResults method missing - may need manual addition"
}

if ($content -notlike "*public void NavigateToAllMovies*")
{
    Write-Host "⚠️  WARNING: NavigateToAllMovies method missing - may need manual addition"
}

Write-Host "✅ Checked: $file"
