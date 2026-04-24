# Fix LibraryPage.xaml.cs - Remove SelectAll() call
# Run from project root

$file = "Views/LibraryPage.xaml.cs"
$content = Get-Content -Path $file -Raw

# Find and replace the FocusSearchBox method
$oldMethod = @"
    public void FocusSearchBox()
    {
        SearchBox.Focus(FocusState.Programmatic);
        SearchBox.SelectAll();
    }
"@

$newMethod = @"
    public void FocusSearchBox()
    {
        SearchBox.Focus(FocusState.Programmatic);
    }
"@

$content = $content -replace [regex]::Escape($oldMethod), $newMethod

Set-Content -Path $file -Value $content -Encoding UTF8
Write-Host "✅ Fixed: $file - Removed SelectAll() call"
