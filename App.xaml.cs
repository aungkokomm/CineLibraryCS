using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CineLibraryCS;

public partial class App : Application
{
    public static MainWindow? MainWindow { get; private set; }

    public App()
    {
        Services.StartLog.Begin();
        SynchronizationContext.SetSynchronizationContext(new Services.UiContext(Microsoft.UI.Dispatching.DispatcherQueue.GetForCurrentThread()));
        InitializeComponent();
        UnhandledException += OnUnhandledException;

        // Catch any first-chance / non-WinUI exception during startup so we
        // can leave a breadcrumb file when the app fails to render.
        AppDomain.CurrentDomain.UnhandledException += (_, e) =>
        {
            try { LogStartupCrash(e.ExceptionObject as Exception, "AppDomain"); } catch { }
        };
        TaskScheduler.UnobservedTaskException += (_, e) =>
        {
            try { LogStartupCrash(e.Exception, "TaskScheduler"); } catch { }
        };
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        try
        {
            Services.StartLog.Mark("launched");
            MainWindow = new MainWindow();
            Services.StartLog.Mark("main window built");
            MainWindow.Activate();
            Services.StartLog.Mark("main window activated");
        }
        catch (Exception ex)
        {
            LogStartupCrash(ex, "OnLaunched");
            throw;
        }
    }

    public static void LogStartupCrashStatic(Exception? ex, string source) => LogStartupCrash(ex, source);

    private static void LogStartupCrash(Exception? ex, string source)
    {
        if (ex == null) return;
        try
        {
            var dir = Path.Combine(AppContext.BaseDirectory, "CineLibrary-Data");
            Directory.CreateDirectory(dir);
            var path = Path.Combine(dir, "startup-crash.log");
            // ex.ToString() includes inner exceptions and their stack traces;
            // an unobserved task's AggregateException has none of its own.
            File.AppendAllText(path,
                $"--- {DateTime.Now:o} [{source}] ---\n{ex}\n\n");
        }
        catch { }
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        e.Handled = true; // prevent crash
        LogStartupCrash(e.Exception, "WinUI");
        var msg = $"{e.Exception.GetType().Name}: {e.Exception.Message}\n\n{e.Exception.StackTrace}";
        System.Diagnostics.Debug.WriteLine("UNHANDLED EXCEPTION:\n" + msg);
        try
        {
            var dialog = new ContentDialog
            {
                Title = "Unexpected Error",
                Content = new ScrollViewer
                {
                    Content = new TextBlock
                    {
                        Text = msg,
                        TextWrapping = Microsoft.UI.Xaml.TextWrapping.Wrap,
                        FontSize = 11,
                        FontFamily = new Microsoft.UI.Xaml.Media.FontFamily("Consolas"),
                        IsTextSelectionEnabled = true,
                    },
                    MaxHeight = 400,
                    Padding = new Microsoft.UI.Xaml.Thickness(0, 0, 16, 0),
                },
                CloseButtonText = "OK",
                XamlRoot = MainWindow?.Content?.XamlRoot,
                RequestedTheme = ElementTheme.Dark,
            };
            _ = dialog.ShowAsync();
        }
        catch
        {
            // If dialog itself fails, at least don't crash
            System.Diagnostics.Debug.WriteLine("UNHANDLED: " + msg);
        }
    }
}
