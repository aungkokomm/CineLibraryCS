using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace CineLibraryCS;

public partial class App : Application
{
    public static MainWindow? MainWindow { get; private set; }

    public App()
    {
        InitializeComponent();
        UnhandledException += OnUnhandledException;
    }

    protected override void OnLaunched(LaunchActivatedEventArgs args)
    {
        MainWindow = new MainWindow();
        MainWindow.Activate();
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        e.Handled = true; // prevent crash
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
