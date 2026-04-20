# Fix KeyboardShortcutsDialog.xaml.cs
# Run from project root

$file = "Views/KeyboardShortcutsDialog.xaml.cs"

$content = @"
using Microsoft.UI.Xaml.Controls;

namespace CineLibraryCS.Views;

public sealed partial class KeyboardShortcutsDialog : ContentDialog
{
    public KeyboardShortcutsDialog()
    {
        this.InitializeComponent();
    }
}
"@

Set-Content -Path $file -Value $content -Encoding UTF8
Write-Host "✅ Fixed: $file"
