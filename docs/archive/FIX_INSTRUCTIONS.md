# 🚀 LOCAL FIX SCRIPTS - HOW TO USE

## Quick Start (3 steps)

### Step 1: Run the fix scripts
Open PowerShell in project root and run:
```powershell
.\run-all-fixes.ps1
```

### Step 2: Build the project
```powershell
.\build-and-test.ps1
```

### Step 3: Test in Visual Studio
Open `CineLibraryCS.csproj` in Visual Studio and press F5 to run.

---

## What Each Script Does

### ✅ fix-statistics-page.ps1
- Fixes API calls to use `AppState.Instance.Db`
- Fixes tuple unpacking for `GetMoviesByDecade()`
- Removes broken `SelectAll()` call
- Updates XAML control bindings

### ✅ fix-filter-results-page.ps1
- Fixes `ListOptions` import and usage
- Fixes `MovieDetailDialog` initialization
- Adds correct navigation methods
- Uses `Activate()` instead of `ShowAsync()` for Window

### ✅ fix-library-page.ps1
- Removes `SelectAll()` from `FocusSearchBox()`
- Keeps keyboard shortcut functionality

### ✅ fix-keyboard-shortcuts-dialog.ps1
- Proper namespace declaration
- Minimal implementation for ContentDialog

### ✅ fix-main-window.ps1
- Adds `RefreshSidebar()` method
- Validates other methods exist

### ✅ run-all-fixes.ps1
- Master script that runs all fixes in order
- Shows progress with colored output
- Prints next steps after completion

### ✅ build-and-test.ps1
- Cleans previous build
- Builds project with full error reporting
- Shows success/failure status

---

## Detailed Instructions

### If you're on Windows (recommended):

1. **Open PowerShell** as Administrator
2. **Navigate to project root**:
   ```powershell
   cd E:\CineLibraryCS
   ```

3. **Run all fixes at once**:
   ```powershell
   Set-ExecutionPolicy -ExecutionPolicy Bypass -Scope Process
   .\run-all-fixes.ps1
   ```

4. **Build and test**:
   ```powershell
   .\build-and-test.ps1
   ```

5. **If build succeeds**: Open Visual Studio and press F5

---

## Troubleshooting

### "PowerShell execution policy error"
Run this first:
```powershell
Set-ExecutionPolicy -ExecutionPolicy Bypass -Scope Process
```

### "Script not found"
Make sure you're in the project root (`E:\CineLibraryCS`):
```powershell
dir *.ps1  # Should show all the fix scripts
```

### "Build still has errors"
After running scripts, check these files manually:
- `Views/StatisticsPage.xaml.cs` - Line 46 should use `double`
- `Views/FilterResultsPage.xaml.cs` - Line 37 should have `ListOptions`
- `MainWindow.xaml.cs` - Check navigation methods exist

---

## Manual Fixes (If scripts fail)

### 1. StatisticsPage.xaml.cs - Replace constructor:
```csharp
public StatisticsPage()
{
    this.InitializeComponent();
    _db = AppState.Instance.Db;  // NOT AppState.Services
    this.Loaded += OnPageLoaded;
}
```

### 2. FilterResultsPage.xaml.cs - Fix imports:
```csharp
using CineLibraryCS.ViewModels;  // ADD THIS
// Then use: new ListOptions { PageSize = 100 }
```

### 3. LibraryPage.xaml.cs - Remove SelectAll:
```csharp
public void FocusSearchBox()
{
    SearchBox.Focus(FocusState.Programmatic);
    // DELETE: SearchBox.SelectAll();
}
```

### 4. MovieDetailDialog.xaml.cs - Fix tuple:
```csharp
foreach (var (decade, count, _) in decadeData.OrderByDescending(d => d.decade))
```

---

## Features Being Added (v1.4.0)

✅ **Statistics Dashboard** - View library statistics (📊)
✅ **Keyboard Shortcuts** - Help dialog (?)
✅ **Actor/Director Filters** - Click names to filter
✅ **Quick Search** - Ctrl+F to focus search
✅ **Fast Quit** - Ctrl+Q to close app
✅ **Toggle Sidebar** - Ctrl+L to show/hide

---

## Testing Checklist

After build succeeds, test:
- [ ] Click "📊 Statistics" button - should show dashboard
- [ ] Click "?" button in titlebar - should show shortcuts
- [ ] Press Ctrl+F - search box gets focus
- [ ] Press Ctrl+L - sidebar hides/shows
- [ ] Press Ctrl+Q - app closes
- [ ] Open movie detail → click director name → should filter by director
- [ ] Open movie detail → click actor card → should filter by actor

---

## Commit Changes

After successful build and testing:
```powershell
git add -A
git commit -m "fix: v1.4.0 UI features - Fixed all compilation errors, ready for testing"
```

---

## Need Help?

If something fails:
1. **Read the error message carefully**
2. **Check the file and line number**
3. **Look at the "Manual Fixes" section above**
4. **Compare with original file if needed**

---

## Next Steps After This Completes

1. ✅ Build succeeds (0 errors)
2. 🧪 Test all features work
3. 📝 Update version (already done in .csproj)
4. 🔗 Commit to git
5. 📦 Build installer
6. 🚀 Release v1.4.0 to GitHub

---

**All scripts are ready to run locally. No more Copilot needed!** 🎉
