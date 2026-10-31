using System.Runtime.InteropServices;
using Microsoft.UI.Dispatching;

namespace CineLibraryCS.Services;

/// <summary>
/// v4.10.0: the UI thread's context. While a thread like this one waits for a lock the CLR keeps
/// pumping, and a message sent to a XAML window at that moment is reentrancy: WinUI ends the process
/// ("Reentrancy was detected", exit 0xC000027B, no handler, no Windows report). The database lock
/// makes that wait routine: a card asks for its poster during layout while a search is reading.
/// Waiting without pumping, as WPF does, the UI thread just waits its turn.
/// </summary>
internal sealed class UiContext : DispatcherQueueSynchronizationContext
{
    public UiContext(DispatcherQueue queue) : base(queue) => SetWaitNotificationRequired();

    public override int Wait(IntPtr[] waitHandles, bool waitAll, int millisecondsTimeout) =>
        WaitForMultipleObjectsEx(waitHandles.Length, waitHandles, waitAll, millisecondsTimeout, false);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern int WaitForMultipleObjectsEx(int count, IntPtr[] handles, bool waitAll, int milliseconds, bool alertable);
}
